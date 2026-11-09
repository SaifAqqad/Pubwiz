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
/// <param name="Os">
/// The OS to build for when a project's target doesn't decide it (<c>--os</c>, a preset, or the confirm page);
/// <c>null</c> for this machine's. See <see cref="PublishPlatform"/>.
/// </param>
/// <param name="Architecture">
/// The architecture to build for, e.g. <c>arm64</c> (<c>--arch</c>, a preset, or the confirm page); <c>null</c> for
/// each target's default.
/// </param>
public sealed record PublishBatch(IReadOnlyList<PublishSelection> Selections, string? Configuration = null, int? Parallel = null, string? PresetFilePath = null,
    PresetSource? Preset = null, PublishOs? Os = null, string? Architecture = null)
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

    /// <summary>What <see cref="OfferAsync"/> did.</summary>
    /// <param name="Shown">The preset list was shown (so the wizard shouldn't offer the last publish again).</param>
    /// <param name="Batch">The chosen preset or last publish, or <c>null</c> to continue with the wizard.</param>
    public sealed record Offer(bool Shown, PublishBatch? Batch);

    /// <summary>
    /// When a <c>pubwiz.json</c> with presets exists, offers to run one instead of choosing projects, with the last
    /// publish from here (<paramref name="recent"/>) at the top.
    /// </summary>
    public async Task<Offer> OfferAsync(string startDirectory, RecentRun? recent, CancellationToken cancellationToken)
    {
        if (PresetFile.Find(startDirectory) is not { } filePath || Load(filePath) is not { Presets.Count: > 0 } file)
        {
            return new Offer(Shown: false, Batch: null);
        }

        const string chooseProjects = "\0choose";
        const string publishAgain = "\0again";
        var prompt = Questions.Menu<string>("Run a saved preset?")
            .UseConverter(name => name switch
            {
                chooseProjects => "[dim]No, choose projects[/]",
                publishAgain => $"[{Widgets.PrimaryColorMarkup}]↻ Repeat last publish[/] [dim]{DescribeRecent(recent!, DateTimeOffset.Now).EscapeMarkup()}[/]",
                _ => $"{name.EscapeMarkup()} [dim]({DescribeProjects(file.Presets[name]).EscapeMarkup()})[/]"
            });

        if (recent is not null)
        {
            prompt.AddChoice(publishAgain);
        }

        var choice = console.Prompt(prompt.AddChoices(file.Presets.Keys.Order()).AddChoices(chooseProjects));
        return choice switch
        {
            chooseProjects => new Offer(Shown: true, Batch: null),
            publishAgain => new Offer(Shown: true, await TryResolveRecentAsync(recent!, filePath, cancellationToken)),
            _ => new Offer(Shown: true, await ResolveAsync(choice, file.Presets[choice], filePath, cancellationToken))
        };
    }

    /// <summary>
    /// The last publish from here as a batch, or <c>null</c> (after saying why) when it can't be published any more,
    /// e.g. a project was moved or a profile renamed.
    /// </summary>
    /// <param name="presetFilePath">Where the batch can be saved as a preset.</param>
    public async Task<PublishBatch?> TryResolveRecentAsync(RecentRun recent, string? presetFilePath, CancellationToken cancellationToken)
    {
        // The paths are full paths, so the base folder doesn't matter.
        var resolved = await console.Status().StartAsync("Loading the last publish...",
            _ => resolver.ResolveAsync(recent.ToPreset(), Directory.GetCurrentDirectory(), cancellationToken));

        if (!resolved.IsValid)
        {
            var problems = string.Join(Environment.NewLine, resolved.Problems.Select(p => $"  - {DisplayText.ShortPath(p.Entry.Path)} ({p.Entry.Profile}): {p.Reason}"));
            console.Write(Widgets.Warning($"The last publish can't be repeated:{Environment.NewLine}{problems}"));
            console.WriteLine();
            return null;
        }

        var selections = resolved.Items.Select(i => new PublishSelection(i.Project, i.Profile) { Order = i.Order }).ToList();
        return new PublishBatch(selections, recent.Configuration, recent.Parallel, presetFilePath, Os: PublishPlatform.ParseOs(recent.Os),
            Architecture: PublishPlatform.ParseArchitecture(recent.Arch));
    }

    /// <summary>The last publish, e.g. "Shop.Api with Production · 2 hours ago" or "Api + Worker → Web · yesterday".</summary>
    internal static string DescribeRecent(RecentRun recent, DateTimeOffset now)
    {
        var projects = DescribeProjects(recent.ToPreset());
        var profiles = recent.Projects.Select(p => p.Profile).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var profile = profiles.Count == 1 ? $" with {profiles[0]}" : string.Empty;
        return $"{DisplayText.Truncate(projects, 60)}{profile} · {DisplayText.RelativeTime(recent.PublishedAt, now)}";
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
    /// <param name="os">The OS chosen to build for; not saved when it's this machine's.</param>
    /// <param name="architecture">The architecture chosen to build for; not saved when it's each target's default.</param>
    public void Save(IReadOnlyList<PublishSelection> selections, string filePath, string? suggestedName = null, int? parallel = null,
        IReadOnlyList<int>? steps = null, PublishOs? os = null, string? architecture = null)
    {
        var file = PresetFile.LoadOrCreate(filePath);
        var name = console.Ask("Preset name", defaultValue: suggestedName, validate: n => string.IsNullOrWhiteSpace(n) ? "Enter a name" : null);

        if (file.Presets.ContainsKey(name) && !console.AskYesNo($"Replace the existing preset '{name}'?", defaultValue: false))
        {
            return;
        }

        var baseDirectory = Path.GetDirectoryName(filePath)!;
        var projects = selections.Select((s, i) => PresetResolver.CreateEntry(s.Project, s.Profile, baseDirectory, steps?[i])).ToList();
        file.Presets[name] = new PublishPreset(projects, Parallel: parallel, Os: os is { } chosen ? PublishPlatform.ToIdentifier(chosen) : null,
            Arch: architecture);
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
        return new PublishBatch(selections, preset.Configuration, preset.Parallel, Preset: new PresetSource(name, filePath),
            Os: PublishPlatform.ParseOs(preset.Os), Architecture: PublishPlatform.ParseArchitecture(preset.Arch));
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
