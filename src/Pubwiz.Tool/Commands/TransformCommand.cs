using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Transforms;
using Pubwiz.Tool.Rendering;
using Pubwiz.Tool.Wizard;
using System.ComponentModel;
using Pubwiz.Tool.Infrastructure;

namespace Pubwiz.Tool.Commands;

/// <summary>
/// <c>dotnet pubwiz transform</c>: writes a transformed web.config without publishing,
/// using the same transform files and order as <c>dotnet publish</c>.
/// </summary>
public sealed class TransformCommand(IAnsiConsole console, WorkspacePage workspacePage) : AsyncCommand<TransformCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[path]")]
        [Description("Folder, solution or project containing the web.config. Defaults to the current folder")]
        public string? Path { get; init; }

        [CommandOption("-c|--configuration")]
        [Description("Applies web.<configuration>.config (asked for when omitted)")]
        public string? Configuration { get; init; }

        [CommandOption("-p|--profile")]
        [Description("Applies web.<profile>.config and Properties/PublishProfiles/<profile>.transform")]
        public string? Profile { get; init; }

        [CommandOption("-e|--environment")]
        [Description("Applies web.<environment>.config")]
        public string? Environment { get; init; }

        [CommandOption("-t|--transform <FILE>")]
        [Description("An extra transform file to apply last (repeatable)")]
        public string[] Transforms { get; init; } = [];

        [CommandOption("-o|--output <FILE>")]
        [Description("Where to write the result. Defaults to obj/pubwiz/transformed/web.config in the project")]
        public string? Output { get; init; }

        [CommandOption("--print")]
        [Description("Also print the transformed file")]
        public bool Print { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Widgets.WriteHeader();

        var workspace = await workspacePage.LoadAsync(settings.Path ?? Directory.GetCurrentDirectory(), cancellationToken);
        var project = ChooseProject(workspace);
        var webConfig = FindWebConfig(project) ?? throw new UserErrorException($"{project.Name} has no web.config.");

        var configuration = settings.Configuration ?? CommonPrompts.ChooseConfiguration(console);
        var profile = settings.Profile ?? ChooseProfile(project);
        var environment = settings.Environment ?? AskForEnvironment();

        var transforms = WebConfigTransformPlanner.Plan(project.Directory, configuration, profile, environment).ToList();
        transforms.AddRange(settings.Transforms.Select(t => new PlannedTransform(TransformKind.Custom, UserPath.ToFullPath(t))));

        if (transforms.Count == 0)
        {
            console.MarkupLine("[yellow]No transform files apply, so the output is a copy of web.config.[/]");
        }
        else
        {
            WritePlan(transforms);
            WarnAboutCasing(transforms);
        }

        var output = settings.Output is not null ? UserPath.ToFullPath(settings.Output) : Path.Combine(project.Directory, "obj", "pubwiz", "transformed", "web.config");
        var warnings = WebConfigTransformer.Transform(webConfig, transforms.Select(t => t.FilePath).ToList(), output);

        foreach (var warning in warnings)
        {
            console.Write(Widgets.Warning(warning));
            console.WriteLine();
        }

        console.MarkupLine($"{Widgets.SuccessCheckMarkup} Wrote [{Widgets.PrimaryColorMarkup}]{DisplayText.Link(output)}[/]");
        if (settings.Print)
        {
            // Written to the raw output writer: the console's word wrapping would break up long XML lines.
            var writer = console.Profile.Out.Writer;
            await writer.WriteLineAsync();
            await writer.WriteLineAsync(await File.ReadAllTextAsync(output, cancellationToken));
        }

        return ExitCodes.Success;
    }

    private ProjectInfo ChooseProject(Workspace workspace)
    {
        var candidates = workspace.Projects.Where(p => FindWebConfig(p) is not null).ToList();
        return candidates.Count switch
        {
            0 => throw new UserErrorException("No project with a web.config was found."),
            1 => candidates[0],
            _ => console.Choose("Project", candidates, p => p.Name.EscapeMarkup())
        };
    }

    private string? ChooseProfile(ProjectInfo project)
    {
        var profiles = PublishProfileStore.List(project.Directory);
        if (profiles.Count == 0)
        {
            return null;
        }

        const string none = "(no profile)";
        var choice = console.Choose("Publish profile (its transforms are applied too)", [none, .. profiles.Select(p => p.Name)]);

        return choice == none ? null : choice;
    }

    private string? AskForEnvironment()
    {
        var environment = console.Ask("Environment", hint: "optional, e.g. Staging", optional: true);
        return environment.Length > 0 ? environment : null;
    }

    private void WritePlan(IReadOnlyList<PlannedTransform> transforms)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(new Style(Widgets.PrimaryColor, decoration: Decoration.Dim))
            .AddColumns("#", "Transform", "Applied because");

        for (var i = 0; i < transforms.Count; i++)
        {
            table.AddRow((i + 1).ToString(), DisplayText.Link(transforms[i].FilePath), transforms[i].Kind.ToString());
        }

        console.Write(table);
    }

    /// <summary>
    /// dotnet publish looks for transform files by their exact name, so on case-sensitive file systems (usually Linux)
    /// it skips e.g. "Web.Release.config".
    /// </summary>
    private void WarnAboutCasing(IReadOnlyList<PlannedTransform> transforms)
    {
        foreach (var transform in transforms.Where(t => !t.FoundByDotnetPublish))
        {
            console.Write(Widgets.Warning($"{Path.GetFileName(transform.FilePath)} is applied here, but dotnet publish on this OS only finds the exact name (e.g. web.Release.config) and will skip it. Rename the file to fix that."));
            console.WriteLine();
        }
    }

    /// <summary>Case-insensitive: projects created on Windows often have "Web.config".</summary>
    private static string? FindWebConfig(ProjectInfo project) =>
        Directory.EnumerateFiles(project.Directory, "*.config")
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), "web.config", StringComparison.OrdinalIgnoreCase));
}
