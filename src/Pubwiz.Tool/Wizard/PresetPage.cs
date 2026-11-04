using Pubwiz.Core.Discovery;
using Pubwiz.Core.Presets;
using Pubwiz.Core.Publishing;
using Pubwiz.Tool.Rendering;
using Pubwiz.Tool.Infrastructure;

namespace Pubwiz.Tool.Wizard;

/// <summary>The projects to publish and the batch options, however they were chosen.</summary>
/// <param name="Selections">Projects with their profiles and orders.</param>
/// <param name="Configuration">A configuration override from a preset.</param>
/// <param name="Parallel">The most projects to publish at the same time in a step, from a preset or <c>--parallel</c>.</param>
/// <param name="PresetFilePath">Where the selection can be saved as a preset; <c>null</c> when it came from a preset or the command line.</param>
/// <param name="Preset">The preset it came from, whose order can be updated from the confirm page.</param>
public sealed record PublishBatch(IReadOnlyList<PublishSelection> Selections, string? Configuration = null, int? Parallel = null, string? PresetFilePath = null,
    PresetSource? Preset = null)
{
    /// <summary>The step each selection is published in (1, 2, ... without gaps), in selection order.</summary>
    public IReadOnlyList<int> Steps => PublishOrder.ToSteps(Selections.Select(s => s.Order).ToList(), Parallel);

    /// <summary>The same batch with these orders (one per selection, in selection order).</summary>
    public PublishBatch WithOrders(IReadOnlyList<int> orders) =>
        this with { Selections = Selections.Select((selection, i) => selection with { Order = orders[i] }).ToList() };
}

/// <summary>A preset in a preset file.</summary>
public sealed record PresetSource(string Name, string FilePath);

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
        var filePath = presetFilePath is not null ? UserPath.ToFullPath(presetFilePath) : PresetFile.Find(startDirectory);
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
        var choice = console.Prompt(Questions.Menu<string>("Run a saved preset?")
            .AddChoices(file.Presets.Keys.Order())
            .AddChoices(chooseProjects)
            .UseConverter(name => name == chooseProjects ? "[dim]No, choose projects[/]" : $"{name.EscapeMarkup()} [dim]({DescribeProjects(file.Presets[name]).EscapeMarkup()})[/]"));

        return choice == chooseProjects ? null : await ResolveAsync(choice, file.Presets[choice], filePath, cancellationToken);
    }

    /// <summary>
    /// The <c>pubwiz.json</c> that presets are saved to: an existing one in the start folder or a parent folder,
    /// otherwise a new one next to the solution (or in the start folder).
    /// </summary>
    /// <param name="workspace">The loaded workspace.</param>
    /// <param name="startPath">The folder, solution or project Pubwiz was started with.</param>
    public static string GetPresetFilePath(Workspace workspace, string startPath)
    {
        var fullPath = Path.GetFullPath(startPath);
        var startDirectory = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath)!;

        return PresetFile.Find(startDirectory)
               ?? Path.Combine(workspace.SolutionPath is null ? startDirectory : Path.GetDirectoryName(workspace.SolutionPath)!, PresetFile.FileName);
    }

    /// <summary>Asks for a name and saves the selection as a preset.</summary>
    /// <param name="selections">The projects and profiles to save.</param>
    /// <param name="filePath">From <see cref="GetPresetFilePath"/>.</param>
    /// <param name="suggestedName">The default name, if any.</param>
    /// <param name="parallel">The most projects to publish at the same time in a step; not saved when there's no limit.</param>
    /// <param name="steps">The step of each selection; saved as each project's order. Without it, the projects run one after another.</param>
    public void Save(IReadOnlyList<PublishSelection> selections, string filePath, string? suggestedName = null, int? parallel = null,
        IReadOnlyList<int>? steps = null)
    {
        var file = PresetFile.LoadOrCreate(filePath);
        var name = console.Ask("Preset name", defaultValue: suggestedName, validate: n => string.IsNullOrWhiteSpace(n) ? "Enter a name" : null);

        if (file.Presets.ContainsKey(name) && !console.AskYesNo($"Replace the existing preset '{name}'?", defaultValue: false))
        {
            return;
        }

        var baseDirectory = Path.GetDirectoryName(filePath)!;
        var projects = selections.Select((s, i) => PresetResolver.CreateEntry(s.Project, s.Profile, baseDirectory, steps?[i])).ToList();
        file.Presets[name] = new PublishPreset(projects, Parallel: parallel);
        file.Save(filePath);

        console.MarkupLine($"{Widgets.SuccessCheckMarkup} Saved preset [{Widgets.PrimaryColorMarkup}]{name.EscapeMarkup()}[/] in {DisplayText.Link(filePath)}");
        console.MarkupLine($"[dim]Run it with: {ToolInfo.CommandName} --preset {name.EscapeMarkup()}[/]");
    }

    /// <summary>Saves the batch's order to the preset it came from, keeping the preset's other settings.</summary>
    public void SaveOrder(PublishBatch batch)
    {
        var (name, filePath) = batch.Preset ?? throw new InvalidOperationException("The batch didn't come from a preset.");
        var file = Load(filePath);
        if (!file.Presets.TryGetValue(name, out var preset))
        {
            throw new UserErrorException($"The preset '{name}' is no longer in {filePath}.");
        }

        var baseDirectory = Path.GetDirectoryName(filePath)!;
        var steps = batch.Steps;
        var projects = batch.Selections.Select((s, i) => PresetResolver.CreateEntry(s.Project, s.Profile, baseDirectory, steps[i])).ToList();
        file.Presets[name] = preset with { Projects = projects };
        file.Save(filePath);

        console.MarkupLine($"{Widgets.SuccessCheckMarkup} Saved the order to preset [{Widgets.PrimaryColorMarkup}]{name.EscapeMarkup()}[/]");
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
        var selections = resolved.Items.Select(i => new PublishSelection(i.Project, i.Profile) { Order = i.Order }).ToList();
        return new PublishBatch(selections, preset.Configuration, preset.Parallel, Preset: new PresetSource(name, filePath));
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

    /// <summary>The projects by step, e.g. "Api + Worker → Web".</summary>
    internal static string DescribeProjects(PublishPreset preset)
    {
        var steps = PublishOrder.ToSteps(preset.Projects.Select(p => p.Order).ToList(), preset.Parallel);
        return string.Join(" → ", preset.Projects
            .Select((project, i) => (Name: Path.GetFileNameWithoutExtension(project.Path), Step: steps[i]))
            .GroupBy(p => p.Step)
            .OrderBy(step => step.Key)
            .Select(step => string.Join(" + ", step.Select(p => p.Name))));
    }
}
