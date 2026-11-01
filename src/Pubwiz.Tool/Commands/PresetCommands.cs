using Pubwiz.Core.Presets;
using Pubwiz.Tool.Rendering;
using System.ComponentModel;
using Pubwiz.Tool.Infrastructure;

namespace Pubwiz.Tool.Commands;

/// <summary>Options shared by the <c>presets</c> commands.</summary>
public class PresetSettings : CommandSettings
{
    [CommandOption("--preset-file <FILE>")]
    [Description("The preset file (default: pubwiz.json in the current folder or its parents)")]
    public string? PresetFile { get; init; }

    /// <exception cref="UserErrorException">No preset file was found.</exception>
    public string ResolvePresetFile()
    {
        var path = PresetFile is not null ? UserPath.ToFullPath(PresetFile) : Core.Presets.PresetFile.Find(Directory.GetCurrentDirectory());
        return path is not null && File.Exists(path)
            ? path
            : throw new UserErrorException($"No {Core.Presets.PresetFile.FileName} was found. Save a preset from the wizard first.");
    }
}

/// <summary><c>dotnet pubwiz presets list</c>: shows the saved presets.</summary>
public sealed class PresetsListCommand(IAnsiConsole console) : Command<PresetSettings>
{
    public override int Execute(CommandContext context, PresetSettings settings, CancellationToken cancellationToken)
    {
        var path = settings.ResolvePresetFile();
        var file = PresetFile.Load(path);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(new Style(Widgets.PrimaryColor, decoration: Decoration.Dim))
            .AddColumns("Preset", "Projects", "Configuration", "Parallel");

        foreach (var (name, preset) in file.Presets.OrderBy(p => p.Key))
        {
            var projects = string.Join(Environment.NewLine, preset.Projects.Select(p => $"{p.Path} ({p.Profile})"));
            table.AddRow(name.EscapeMarkup(), projects.EscapeMarkup(), preset.Configuration ?? "[dim]profile[/]", preset.Parallel?.ToString() ?? "1");
        }

        console.MarkupLine($"[dim]{DisplayText.Link(Path.GetFullPath(path))}[/]");
        console.Write(table);
        return ExitCodes.Success;
    }
}

/// <summary><c>dotnet pubwiz presets export</c>: writes one preset to its own file, to share or to run with <c>--preset-file</c>.</summary>
public sealed class PresetsExportCommand(IAnsiConsole console) : Command<PresetsExportCommand.Settings>
{
    public sealed class Settings : PresetSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("The preset to export")]
        public string Name { get; init; } = string.Empty;

        [CommandOption("-o|--output <FILE>")]
        [Description("Where to write it (default: <name>.pubwiz.json)")]
        public string? Output { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var sourcePath = settings.ResolvePresetFile();
        var file = PresetFile.Load(sourcePath);
        if (!file.Presets.ContainsKey(settings.Name))
        {
            throw new UserErrorException($"There's no preset named '{settings.Name}'. Available: {string.Join(", ", file.Presets.Keys)}.");
        }

        // Paths in a preset are relative to its file, so the export goes in the same folder unless asked otherwise.
        var output = settings.Output is not null ? UserPath.ToFullPath(settings.Output) : Path.Combine(Path.GetDirectoryName(sourcePath)!, $"{settings.Name}.pubwiz.json");
        if (!string.Equals(Path.GetDirectoryName(output), Path.GetDirectoryName(sourcePath), StringComparison.Ordinal))
        {
            console.Write(Widgets.Warning("The export is in another folder than the original, but its project paths are relative to the original folder."));
            console.WriteLine();
        }

        file.Export(settings.Name).Save(output);
        console.MarkupLine($"{Widgets.SuccessCheckMarkup} Exported [{Widgets.PrimaryColorMarkup}]{settings.Name.EscapeMarkup()}[/] to {DisplayText.Link(output)}");
        console.MarkupLine($"[dim]Run it with: {ToolInfo.CommandName} --preset-file {DisplayText.ShortPath(output).EscapeMarkup()}[/]");
        return ExitCodes.Success;
    }
}
