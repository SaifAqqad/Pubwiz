using System.Text.Json;
using System.Xml.Linq;

namespace Pubwiz.Core.Dotnet;

/// <summary>
/// The files for evaluating many projects with a single <c>dotnet msbuild</c> run.
/// One run per project is slow, because every run starts MSBuild and loads the SDK files again.
/// </summary>
/// <remarks>
/// How it works:
/// <list type="number">
///   <item>An "inspect" target is added to every project through MSBuild's <c>CustomAfterMicrosoftCommonTargets</c> hook
///   (and its multi-targeting twin). It returns one item with the requested properties and items as metadata.</item>
///   <item>A runner project calls that target on each project with the MSBuild task, and <c>-getTargetResult</c> prints
///   the collected items as JSON.</item>
/// </list>
/// The runner calls the projects one at a time (task batching on <c>%(Identity)</c>): when they're all passed to one
/// MSBuild task call, a single broken project makes the call return nothing for the others.
/// The files live in a temporary folder that is deleted on <see cref="Dispose"/>.
/// </remarks>
internal sealed class MsBuildBatch : IDisposable
{
    /// <summary>The runner project's target, which returns the results.</summary>
    public const string RunnerTarget = "PubwizEvaluateAll";

    private const string InspectTarget = "PubwizInspect";
    private const string ResultItem = "PubwizEvaluation";

    /// <summary>Requested items are returned as metadata named <c>Item_{type}</c>, so they can't clash with a property.</summary>
    private const string ItemMetadataPrefix = "Item_";

    private readonly string _folder;

    private MsBuildBatch(string folder, string runnerPath)
    {
        _folder = folder;
        RunnerPath = runnerPath;
    }

    /// <summary>The project to pass to <c>dotnet msbuild</c>.</summary>
    public string RunnerPath { get; }

    /// <summary>Writes the runner project and the inspect target to a new temporary folder.</summary>
    public static MsBuildBatch Create(IEnumerable<string> projectPaths, IReadOnlyCollection<string> properties, IReadOnlyCollection<string> items)
    {
        var folder = Directory.CreateTempSubdirectory("pubwiz-evaluate-").FullName;
        var targetsPath = Path.Combine(folder, "inspect.targets");
        var runnerPath = Path.Combine(folder, "evaluate.proj");

        CreateInspectTargets(properties, items).Save(targetsPath);
        CreateRunner(projectPaths, targetsPath).Save(runnerPath);

        return new MsBuildBatch(folder, runnerPath);
    }

    /// <summary>
    /// Reads the JSON printed by <c>-getTargetResult</c>:
    /// <c>{ "TargetResults": { "PubwizEvaluateAll": { "Items": [ { "MSBuildSourceProjectFile": "...", "OutputType": "Exe", ... } ] } } }</c>
    /// </summary>
    /// <returns>The evaluations by full project path; projects that failed to evaluate are missing. Empty when the output isn't JSON.</returns>
    public static IReadOnlyDictionary<string, MsBuildEvaluation> ParseResults(string json, IReadOnlyCollection<string> properties, IReadOnlyCollection<string> items)
    {
        var results = new Dictionary<string, MsBuildEvaluation>(StringComparer.OrdinalIgnoreCase);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return results;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("TargetResults", out var targets)
                || !targets.TryGetProperty(RunnerTarget, out var target)
                || !target.TryGetProperty("Items", out var resultItems))
            {
                return results;
            }

            foreach (var item in resultItems.EnumerateArray())
            {
                // The MSBuild task adds the project each item came from.
                var projectPath = GetString(item, "MSBuildSourceProjectFile");
                if (projectPath.Length > 0)
                {
                    results[Path.GetFullPath(projectPath)] = ToEvaluation(item, properties, items);
                }
            }
        }

        return results;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover file in the temp folder is harmless.
        }
    }

    /// <summary>
    /// <code>
    /// &lt;Target Name="PubwizInspect" Returns="@(PubwizEvaluation)"&gt;
    ///   &lt;PropertyGroup&gt;&lt;_PubwizPackageReference&gt;@(PackageReference)&lt;/_PubwizPackageReference&gt;&lt;/PropertyGroup&gt;
    ///   &lt;ItemGroup&gt;
    ///     &lt;PubwizEvaluation Include="$(MSBuildProjectFullPath)"&gt;
    ///       &lt;OutputType&gt;$(OutputType)&lt;/OutputType&gt; ...
    ///       &lt;Item_PackageReference&gt;$(_PubwizPackageReference)&lt;/Item_PackageReference&gt;
    ///     &lt;/PubwizEvaluation&gt;
    ///   &lt;/ItemGroup&gt;
    /// &lt;/Target&gt;
    /// </code>
    /// Items are copied into a property first, which joins their names with ';'.
    /// </summary>
    private static XDocument CreateInspectTargets(IReadOnlyCollection<string> properties, IReadOnlyCollection<string> items)
    {
        var itemLists = new XElement("PropertyGroup", items.Select(type => new XElement($"_Pubwiz{type}", $"@({type})")));

        var result = new XElement(ResultItem, new XAttribute("Include", "$(MSBuildProjectFullPath)"),
            properties.Select(name => new XElement(name, $"$({name})")),
            items.Select(type => new XElement(ItemMetadataPrefix + type, $"$(_Pubwiz{type})")));

        return new XDocument(new XElement("Project",
            new XElement("Target", new XAttribute("Name", InspectTarget), new XAttribute("Returns", $"@({ResultItem})"),
                itemLists,
                new XElement("ItemGroup", result))));
    }

    /// <summary>
    /// <code>
    /// &lt;ItemGroup&gt;&lt;PubwizProject Include="/src/Api/Api.csproj" /&gt; ...&lt;/ItemGroup&gt;
    /// &lt;Target Name="PubwizEvaluateAll" Returns="@(PubwizEvaluation)"&gt;
    ///   &lt;MSBuild Projects="%(PubwizProject.Identity)" Targets="PubwizInspect" Properties="CustomAfterMicrosoftCommonTargets=..." ...&gt;
    ///     &lt;Output TaskParameter="TargetOutputs" ItemName="PubwizEvaluation" /&gt;
    ///   &lt;/MSBuild&gt;
    /// &lt;/Target&gt;
    /// </code>
    /// </summary>
    private static XDocument CreateRunner(IEnumerable<string> projectPaths, string targetsPath)
    {
        var escapedTargets = Escape(targetsPath);
        var globalProperties = $"CustomAfterMicrosoftCommonTargets={escapedTargets};CustomAfterMicrosoftCommonCrossTargetingTargets={escapedTargets}";

        var msbuildTask = new XElement("MSBuild",
            new XAttribute("Projects", "%(PubwizProject.Identity)"),
            new XAttribute("Targets", InspectTarget),
            new XAttribute("Properties", globalProperties),
            // A project that fails (or has no inspect target) is skipped; the caller evaluates it on its own to get the error.
            new XAttribute("ContinueOnError", "true"),
            new XAttribute("SkipNonexistentTargets", "true"),
            new XElement("Output", new XAttribute("TaskParameter", "TargetOutputs"), new XAttribute("ItemName", ResultItem)));

        return new XDocument(new XElement("Project",
            new XElement("ItemGroup", projectPaths.Select(path => new XElement("PubwizProject", new XAttribute("Include", Escape(path))))),
            new XElement("Target", new XAttribute("Name", RunnerTarget), new XAttribute("Returns", $"@({ResultItem})"), msbuildTask)));
    }

    private static MsBuildEvaluation ToEvaluation(JsonElement item, IReadOnlyCollection<string> properties, IReadOnlyCollection<string> items)
    {
        var propertyValues = properties.ToDictionary(name => name, name => GetString(item, name), StringComparer.OrdinalIgnoreCase);

        var itemValues = items.ToDictionary(
            type => type,
            IReadOnlyList<MsBuildItem> (type) => GetString(item, ItemMetadataPrefix + type)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(identity => new MsBuildItem(identity, new Dictionary<string, string>()))
                .ToList(),
            StringComparer.OrdinalIgnoreCase);

        return new MsBuildEvaluation(propertyValues, itemValues);
    }

    private static string GetString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;

    /// <summary>
    /// Escapes MSBuild's special characters (<c>% $ @ ' ; ? *</c>) so a path is taken literally.
    /// </summary>
    private static string Escape(string value)
    {
        // '%' first, so the escapes added below aren't escaped again.
        return value
            .Replace("%", "%25")
            .Replace("$", "%24")
            .Replace("@", "%40")
            .Replace("'", "%27")
            .Replace(";", "%3B")
            .Replace("?", "%3F")
            .Replace("*", "%2A");
    }
}
