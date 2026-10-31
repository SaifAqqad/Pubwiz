using Pubwiz.Core.Discovery;
using Pubwiz.Core.Presets;

namespace Pubwiz.Tool.Wizard;

/// <summary>The projects to publish and the batch options, however they were chosen.</summary>
/// <param name="Selections">Projects with their profiles.</param>
/// <param name="Configuration">A configuration override from a preset.</param>
/// <param name="Parallel">A parallelism setting from a preset.</param>
public sealed record PublishBatch(IReadOnlyList<PublishSelection> Selections, string? Configuration = null, int? Parallel = null);

/// <summary>
/// Runs and saves batch presets (<c>pubwiz.json</c>).
/// </summary>
public sealed class PresetPage(IAnsiConsole console, PresetResolver resolver)
{
    /// <summary>
    /// Loads a preset by name, from <paramref name="presetFilePath"/> or the <c>pubwiz.json</c> found from <paramref name="startDirectory"/>.
    /// </summary>
    /// <exception cref="UserErrorException">The file or preset doesn't exist, or an entry can't be resolved.</exception>
    public async Task<PublishBatch> LoadAsync(string? presetName, string? presetFilePath, string startDirectory, CancellationToken cancellationToken)
    {
        var filePath = presetFilePath is not null ? Path.GetFullPath(presetFilePath) : PresetFile.Find(startDirectory);
        if (filePath is null || !File.Exists(filePath))
        {
            throw new UserErrorException(presetFilePath is null
                ? $"No {PresetFile.FileName} was found in '{startDirectory}' or its parent folders."
                : $"'{presetFilePath}' doesn't exist.");
        }

        var file = Load(filePath);
        var name = presetName ?? (file.Presets.Count == 1 ? file.Presets.Keys.First() : null)
            ?? throw new UserErrorException($"{Path.GetFileName(filePath)} has several presets; choose one with --preset ({string.Join(", ", file.Presets.Keys)}).");

        if (!file.Presets.TryGetValue(name, out var preset))
        {
            throw new UserErrorException($"There's no preset named '{name}' in {filePath}. Available: {string.Join(", ", file.Presets.Keys)}.");
        }

        return await ResolveAsync(name, preset, filePath, cancellationToken);
    }

    /// <summary>
    /// When a <c>pubwiz.json</c> with presets exists, offers to run one instead of choosing projects.
    /// </summary>
    /// <returns>The chosen preset's batch, or <c>null</c> to continue with the wizard.</returns>
    public async Task<PublishBatch?> OfferAsync(string startDirectory, CancellationToken cancellationToken)
    {
        if (PresetFile.Find(startDirectory) is not { } filePath || Load(filePath) is not { Presets.Count: > 0 } file)
        {
            return null;
        }

        const string chooseProjects = "\0choose";
        var choice = console.Prompt(new SelectionPrompt<string>()
            .Title("Run a saved preset?")
            .HighlightStyle(new Style(Widgets.PrimaryColor))
            .AddChoices(file.Presets.Keys.Order())
            .AddChoices(chooseProjects)
            .UseConverter(name => name == chooseProjects ? "[dim]No, choose projects[/]" : $"{name.EscapeMarkup()} [dim]({DescribeProjects(file.Presets[name])})[/]"));

        return choice == chooseProjects ? null : await ResolveAsync(choice, file.Presets[choice], filePath, cancellationToken);
    }

    /// <summary>Offers to save the wizard's choices as a preset, in the <c>pubwiz.json</c> of the workspace.</summary>
    public void OfferSave(IReadOnlyList<PublishSelection> selections, Workspace workspace, string startDirectory)
    {
        if (!console.Confirm("Save this selection as a preset for next time?", defaultValue: false))
        {
            return;
        }

        // An existing pubwiz.json wins; otherwise it goes next to the solution (or in the start folder).
        var filePath = PresetFile.Find(startDirectory)
                       ?? Path.Combine(workspace.SolutionPath is null ? Path.GetFullPath(startDirectory) : Path.GetDirectoryName(workspace.SolutionPath)!, PresetFile.FileName);
        var file = PresetFile.LoadOrCreate(filePath);

        var name = console.Prompt(new TextPrompt<string>("Preset name:")
            .Validate(n => string.IsNullOrWhiteSpace(n) ? ValidationResult.Error("[red]Enter a name[/]") : ValidationResult.Success()));

        if (file.Presets.ContainsKey(name) && !console.Confirm($"Replace the existing preset '{name.EscapeMarkup()}'?", defaultValue: false))
        {
            return;
        }

        var baseDirectory = Path.GetDirectoryName(filePath)!;
        file.Presets[name] = new PublishPreset(selections.Select(s => PresetResolver.CreateEntry(s.Project, s.Profile, baseDirectory)).ToList());
        file.Save(filePath);

        console.MarkupLine($"{Widgets.SuccessCheckMarkup} Saved preset [{Widgets.PrimaryColorMarkup}]{name.EscapeMarkup()}[/] in {filePath.EscapeMarkup()}");
        console.MarkupLine($"[dim]Run it with: {ToolInfo.CommandName} --preset {name.EscapeMarkup()}[/]");
    }

    private async Task<PublishBatch> ResolveAsync(string name, PublishPreset preset, string filePath, CancellationToken cancellationToken)
    {
        var resolved = await console.Status().StartAsync($"Loading preset {name}...",
            _ => resolver.ResolveAsync(preset, Path.GetDirectoryName(filePath)!, cancellationToken));

        if (!resolved.IsValid)
        {
            var problems = string.Join(Environment.NewLine, resolved.Problems.Select(p => $"  - {p.Entry.Path} ({p.Entry.Profile}): {p.Reason}"));
            throw new UserErrorException($"The preset '{name}' can't be used:{Environment.NewLine}{problems}");
        }

        console.MarkupLine($"Preset [{Widgets.PrimaryColorMarkup}]{name.EscapeMarkup()}[/]: {resolved.Items.Count} project(s)");
        var selections = resolved.Items.Select(i => new PublishSelection(i.Project, i.Profile)).ToList();
        return new PublishBatch(selections, preset.Configuration, preset.Parallel);
    }

    private static PresetFile Load(string filePath)
    {
        try
        {
            return PresetFile.Load(filePath);
        }
        catch (InvalidDataException ex)
        {
            throw new UserErrorException(ex.Message);
        }
    }

    private static string DescribeProjects(PublishPreset preset) =>
        string.Join(", ", preset.Projects.Select(p => Path.GetFileNameWithoutExtension(p.Path)));
}
