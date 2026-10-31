using Pubwiz.Core.Processes;
using System.Text.Json;

namespace Pubwiz.Core.Dotnet;

/// <summary>
/// Wrapper around the <c>dotnet</c> CLI.
/// </summary>
public sealed class DotnetCli(IProcessRunner processRunner, string executablePath)
{
    public string ExecutablePath { get; } = executablePath;

    /// <summary>
    /// Evaluates a project and returns the requested properties and items, without building it.
    /// </summary>
    /// <exception cref="MsBuildEvaluationException">The project could not be evaluated (invalid project, missing SDK/workload, ...).</exception>
    public async Task<MsBuildEvaluation> EvaluateAsync(string projectPath, IReadOnlyCollection<string> properties, IReadOnlyCollection<string> items, CancellationToken cancellationToken = default)
    {
        // With a single -getProperty, msbuild prints the raw value instead of JSON, so always ask for at least two values.
        var propertyNames = properties.Count + items.Count >= 2 ? properties : [.. properties, "MSBuildProjectName"];

        List<string> arguments = ["msbuild", projectPath, "-nologo"];
        arguments.AddRange(propertyNames.Select(name => $"-getProperty:{name}"));
        arguments.AddRange(items.Select(name => $"-getItem:{name}"));

        var result = await processRunner.RunAsync(new ProcessSpec(ExecutablePath, arguments), cancellationToken: cancellationToken);
        if (!result.Succeeded)
        {
            throw new MsBuildEvaluationException(projectPath, FirstErrorLine(result));
        }

        return ParseEvaluation(result.Output);
    }

    /// <summary>
    /// Runs <c>dotnet publish</c>, streaming every output line to <paramref name="onOutput"/>.
    /// </summary>
    public Task<ProcessResult> PublishAsync(DotnetPublishOptions options, Action<ProcessOutputLine>? onOutput, CancellationToken cancellationToken = default)
    {
        var spec = new ProcessSpec(ExecutablePath, options.ToArguments())
        {
            WorkingDirectory = Path.GetDirectoryName(options.ProjectPath),
            Environment = options.SecretProperties.ToDictionary(p => p.Key, string? (p) => p.Value)
        };

        return processRunner.RunAsync(spec, onOutput, cancellationToken);
    }

    /// <summary>The IDs of the installed workloads (<c>dotnet workload list</c>), e.g. <c>maui-android</c>.</summary>
    public async Task<IReadOnlyList<string>> ListWorkloadsAsync(CancellationToken cancellationToken = default)
    {
        var result = await processRunner.RunAsync(new ProcessSpec(ExecutablePath, ["workload", "list"]), cancellationToken: cancellationToken);
        return result.Succeeded ? ParseWorkloadList(result.Output) : [];
    }

    /// <summary>
    /// Reads the table printed by <c>dotnet workload list</c>: the IDs are the first column of the rows
    /// between the "-----" separator and the first empty line.
    /// </summary>
    internal static IReadOnlyList<string> ParseWorkloadList(string output)
    {
        var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var separator = lines.FindIndex(l => l.TrimStart().StartsWith("---", StringComparison.Ordinal));
        if (separator < 0)
        {
            return [];
        }

        return lines.Skip(separator + 1)
            .TakeWhile(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
            .ToList();
    }

    /// <summary>
    /// Returns the MSBuild errors in a build output (<c>path(line,col): error CODE: message</c>), without duplicates.
    /// </summary>
    public static IReadOnlyList<string> GetBuildErrors(string output)
    {
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Contains(": error ", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Parses the JSON printed by <c>-getProperty</c>/<c>-getItem</c>:
    /// <c>{ "Properties": { "Name": "value" }, "Items": { "Type": [ { "Identity": "...", "Version": "..." } ] } }</c>
    /// </summary>
    internal static MsBuildEvaluation ParseEvaluation(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("Properties", out var propertiesElement))
        {
            foreach (var property in propertiesElement.EnumerateObject())
            {
                properties[property.Name] = property.Value.GetString() ?? string.Empty;
            }
        }

        var items = new Dictionary<string, IReadOnlyList<MsBuildItem>>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("Items", out var itemsElement))
        {
            foreach (var itemType in itemsElement.EnumerateObject())
            {
                items[itemType.Name] = itemType.Value.EnumerateArray().Select(ParseItem).ToList();
            }
        }

        return new MsBuildEvaluation(properties, items);
    }

    private static MsBuildItem ParseItem(JsonElement element)
    {
        var metadata = element.EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.OrdinalIgnoreCase);

        return new MsBuildItem(metadata.GetValueOrDefault("Identity", string.Empty), metadata);
    }

    private static string FirstErrorLine(ProcessResult result)
    {
        // msbuild writes errors to stdout as "path(line,col): error CODE: message".
        var lines = (result.Output + Environment.NewLine + result.Error).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.FirstOrDefault(line => line.Contains("error", StringComparison.OrdinalIgnoreCase))
            ?? lines.FirstOrDefault()
            ?? $"dotnet msbuild exited with code {result.ExitCode}";
    }
}

/// <summary>
/// Thrown when <c>dotnet msbuild</c> cannot evaluate a project.
/// </summary>
public sealed class MsBuildEvaluationException(string projectPath, string error)
    : Exception($"Failed to evaluate '{Path.GetFileName(projectPath)}': {error}")
{
    public string ProjectPath { get; } = projectPath;

    public string Error { get; } = error;
}
