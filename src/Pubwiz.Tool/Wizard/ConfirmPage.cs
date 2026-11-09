using Pubwiz.Core.Infrastructure.Platform;
using Pubwiz.Core.Publishing;
using Pubwiz.Tool.Rendering;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// The last question before publishing, with the batch's steps. The order can be changed here, and a batch picked in
/// the wizard can be saved as a preset (a single project's profile is saved from its profile list instead); a batch
/// from a preset can save its new order back to the preset. The platform each project is built for is shown, and can
/// be changed: the architecture, and the OS for projects whose target doesn't decide it (a folder, FTP).
/// </summary>
public sealed class ConfirmPage(IAnsiConsole console, PresetPage presets, PublishRunner runner)
{
    private const string OrderQuestion = "In what order?";
    private const string PlatformQuestion = "Build for";
    private const string ArchitectureQuestion = "Architecture";

    private enum Choice
    {
        PublishRecommended,
        Publish,
        ChangeOrder,
        ChangePlatform,
        SaveAsPreset,
        SaveOrderToPreset,
        Cancel
    }

    /// <param name="batch">What would be published.</param>
    /// <param name="cancelLabel">The cancel option, e.g. "Cancel" or "Back to projects".</param>
    /// <returns>The batch to publish (with the order chosen here), or <c>null</c> when the user cancels (with the cancel option or Esc).</returns>
    public PublishBatch? Confirm(PublishBatch batch, string cancelLabel)
    {
        console.WriteLine();
        if (batch.Selections.Count > 1)
        {
            WriteSummary(batch);
        }

        WritePlatform(batch);

        var canSavePreset = batch.PresetFilePath is not null && batch.Selections.Count > 1;
        var orderChanged = false;
        while (true)
        {
            // More at once than recommended: the first (default) choice publishes 4 at a time instead.
            List<Choice> choices = PublishPlan.TooManyAtOnce(batch.Steps, batch.Parallel) ? [Choice.PublishRecommended, Choice.Publish] : [Choice.Publish];
            if (batch.Selections.Count > 1)
            {
                choices.Add(Choice.ChangeOrder);
            }

            if (CanChoosePlatform(batch))
            {
                choices.Add(Choice.ChangePlatform);
            }

            if (canSavePreset)
            {
                choices.Add(Choice.SaveAsPreset);
            }

            if (orderChanged && batch.Preset is not null)
            {
                choices.Add(Choice.SaveOrderToPreset);
            }

            choices.Add(Choice.Cancel);

            Choice choice;
            try
            {
                choice = console.Choose(Question(batch), choices, c => c switch
                {
                    Choice.PublishRecommended => $"Publish now, {PublishPlan.RecommendedAtOnce} at a time [dim](recommended)[/]",
                    Choice.Publish when choices.Contains(Choice.PublishRecommended) => "Publish now, with no limit",
                    Choice.Publish => "Publish now",
                    Choice.ChangeOrder => "Change the order",
                    Choice.ChangePlatform => "Change the platform",
                    Choice.SaveAsPreset => "Save this selection as a preset",
                    Choice.SaveOrderToPreset => $"Save the new order to preset '{batch.Preset!.Name.EscapeMarkup()}'",
                    _ => cancelLabel
                });
            }
            catch (PromptCancelledException)
            {
                return null;
            }

            switch (choice)
            {
                case Choice.PublishRecommended:
                    return batch with { Parallel = PublishPlan.RecommendedAtOnce };

                case Choice.Publish:
                    return batch;

                case Choice.ChangeOrder:
                    try
                    {
                        batch = ChooseOrder(batch);
                        orderChanged = true;
                    }
                    catch (PromptCancelledException)
                    {
                        // Esc in the order list keeps the order as it was.
                    }

                    console.WriteLine();
                    WriteSummary(batch);
                    WritePlatform(batch);
                    break;

                case Choice.ChangePlatform:
                    try
                    {
                        batch = ChoosePlatform(batch);
                    }
                    catch (PromptCancelledException)
                    {
                        // Esc keeps the OS as it was.
                    }

                    console.WriteLine();
                    WritePlatform(batch);
                    break;

                case Choice.SaveAsPreset:
                    presets.Save(batch.Selections, batch.PresetFilePath!, parallel: batch.Parallel, steps: batch.Steps, os: batch.Os,
                        architecture: batch.Architecture);
                    canSavePreset = false;
                    console.WriteLine();
                    break;

                case Choice.SaveOrderToPreset:
                    presets.SaveOrder(batch);
                    orderChanged = false;
                    console.WriteLine();
                    break;

                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// Asks in what order to publish the batch's projects, starting from its current order, and writes the answer.
    /// </summary>
    /// <returns>The batch with the chosen order.</returns>
    /// <exception cref="PromptCancelledException">Esc was pressed.</exception>
    public PublishBatch ChooseOrder(PublishBatch batch)
    {
        console.WriteLine();
        var prompt = new OrderPrompt<PublishSelection>($"[bold]{OrderQuestion}[/]", batch.Selections, s => [s.Project.Name, $"{s.Profile.Name} ({s.Profile.Method})"])
        {
            Orders = batch.Steps,
            Parallel = batch.Parallel
        };

        var ordered = batch.WithOrders(prompt.Show(console));
        Questions.WriteAnswer(console, OrderQuestion, Describe(ordered).EscapeMarkup());
        return ordered;
    }

    /// <summary>
    /// The OS and architecture to build for, as one list ("Linux arm64"); only the architecture when every project's
    /// target decides its OS (IIS, Azure). The OS applies to the projects whose target doesn't decide it.
    /// </summary>
    /// <exception cref="PromptCancelledException">Esc was pressed.</exception>
    private PublishBatch ChoosePlatform(PublishBatch batch)
    {
        console.WriteLine();
        var thisMachine = PublishPlatform.ThisMachine is { } machineOs ? $"{PublishPlatform.DisplayName(machineOs)} {OperatingSystemInfo.Architecture}" : "this machine";
        var choosesOs = Resolve(batch).Any(p => p.Platform.CanChooseOs);

        List<PlatformChoice> choices = choosesOs
            ? [new(null, null), .. Enum.GetValues<PublishOs>().SelectMany(os => CommonArchitectures.Select(arch => new PlatformChoice(os, arch)))]
            : [new(null, null), .. CommonArchitectures.Select(arch => new PlatformChoice(null, arch))];

        // The current choice first, so Enter keeps it (added when it's one only --arch gives, like x86).
        var current = new PlatformChoice(choosesOs ? batch.Os : null, batch.Architecture);
        choices.Remove(current);
        choices.Insert(0, current);

        var answer = console.Choose(choosesOs ? PlatformQuestion : ArchitectureQuestion, choices, c => (c.Os, c.Architecture) switch
        {
            (null, null) when choosesOs => $"Default [dim](this machine: {thisMachine}; servers: their OS, x64)[/]",
            (null, null) => "Default [dim](each target's, usually x64)[/]",
            ({ } os, { } arch) => $"{PublishPlatform.DisplayName(os)} {arch}",
            ({ } os, null) => PublishPlatform.DisplayName(os),
            (null, { } arch) => arch
        });

        return choosesOs ? batch with { Os = answer.Os, Architecture = answer.Architecture } : batch with { Architecture = answer.Architecture };
    }

    /// <summary>
    /// What each project is built for, grouped:
    /// <code>
    ///   Platform: Windows x64 · Api, Web
    ///             macOS arm64 (this machine) · Tool
    /// </code>
    /// Without project names when they're all built for the same; nothing when the platform applies to none of them.
    /// </summary>
    private void WritePlatform(PublishBatch batch)
    {
        var groups = Resolve(batch)
            .Where(p => p.Platform.DisplayName is not null)
            .GroupBy(p => Describe(p.Platform))
            .ToList();

        if (groups.Count == 0)
        {
            return;
        }

        const string label = "  Platform: ";
        var showProjects = groups.Count > 1 || groups[0].Count() < batch.Selections.Count;
        for (var i = 0; i < groups.Count; i++)
        {
            var projects = showProjects ? $" [dim]· {string.Join(", ", groups[i].Select(p => p.Selection.Project.Name)).EscapeMarkup()}[/]" : string.Empty;
            var prefix = i == 0 ? $"  [dim]{label.Trim()}[/] " : new string(' ', label.Length);
            console.MarkupLine($"{prefix}{groups[i].Key}{projects}");
        }

        console.WriteLine();
    }

    /// <summary>e.g. "Linux x64", "macOS arm64 (this machine)" or "linux-musl-x64 (profile)", as markup.</summary>
    private static string Describe(ResolvedPlatform platform)
    {
        var name = $"[{Widgets.PrimaryColorMarkup}]{platform.DisplayName.EscapeMarkup()}[/]";
        return platform.Source switch
        {
            PlatformSource.ThisMachine when platform.RuntimeIdentifier is null => $"{name} [dim](this machine)[/]",
            PlatformSource.Profile => $"{name} [dim](profile)[/]",
            _ => name
        };
    }

    /// <summary>Each selection with what it's built for; selections no target publishes are left out.</summary>
    private List<(PublishSelection Selection, ResolvedPlatform Platform)> Resolve(PublishBatch batch) =>
        batch.Selections
            .Where(s => runner.FindTarget(s.Profile) is not null)
            .Select(s => (s, PublishPlatform.Resolve(s.Project, s.Profile, runner.FindTarget(s.Profile)!.GetPlatform(s.Profile), batch.Os, batch.Architecture)))
            .ToList();

    /// <summary>Whether the platform of any project can be changed here (its profile has no runtime of its own).</summary>
    private bool CanChoosePlatform(PublishBatch batch) => Resolve(batch).Any(p => p.Platform.CanChooseArchitecture);

    /// <summary>The architectures offered on the confirm page; x86 and arm are rare, and <c>--arch</c> still gives them.</summary>
    private static readonly string[] CommonArchitectures = ["x64", "arm64"];

    /// <summary>An answer to "Build for": <c>null</c>s for each target's default.</summary>
    private sealed record PlatformChoice(PublishOs? Os, string? Architecture);

    /// <summary>
    /// Lists each project with its profile by step, so the whole batch can be checked before publishing:
    /// <code>
    ///   1 ┌ Api      Prod (WebDeploy)
    ///     └ Worker   Prod (ZipDeploy)
    ///   2 ─ Web      Prod (Folder)
    /// </code>
    /// </summary>
    private void WriteSummary(PublishBatch batch)
    {
        var unicode = console.Profile.Capabilities.Unicode;
        var steps = batch.Steps;

        var grid = new Grid()
            .AddColumn(new GridColumn().PadLeft(2).PadRight(1))
            .AddColumn(new GridColumn().PadLeft(0).PadRight(1))
            .AddColumn(new GridColumn().PadLeft(0).PadRight(3))
            .AddColumn(new GridColumn().PadLeft(0));

        foreach (var step in batch.Selections.Select((selection, i) => (Selection: selection, Step: steps[i])).GroupBy(s => s.Step).OrderBy(s => s.Key))
        {
            var members = step.ToList();
            for (var i = 0; i < members.Count; i++)
            {
                var (project, profile) = members[i].Selection;
                grid.AddRow(
                    new Markup(i == 0 ? $"[{Widgets.PrimaryColorMarkup} bold]{step.Key}[/]" : string.Empty),
                    new Markup($"[{Widgets.PrimaryColorMarkup} dim]{PublishPlan.Connector(i, members.Count, unicode)}[/]"),
                    new Markup($"[bold]{project.Name.EscapeMarkup()}[/]"),
                    new Markup($"[{Widgets.PrimaryColorMarkup}]{profile.Name.EscapeMarkup()}[/] [dim]({profile.Method})[/]"));
            }
        }

        console.Write(grid);
        console.MarkupLine($"  [dim]{Describe(batch).EscapeMarkup()}[/]");
        if (PublishPlan.TooManyAtOnce(steps, batch.Parallel))
        {
            console.MarkupLine($"  {PublishPlan.TooManyAtOnceTip}");
        }

        console.WriteLine();
    }

    private static string Describe(PublishBatch batch)
    {
        var steps = batch.Steps;
        return PublishPlan.Describe(batch.Selections.Select((s, i) => (steps[i], s.Project.Name)).ToList(), batch.Parallel);
    }

    /// <summary>e.g. "Publish 3 projects in 2 steps?"</summary>
    internal static string Question(PublishBatch batch)
    {
        var count = batch.Selections.Count;
        if (count == 1)
        {
            return "Publish 1 project?";
        }

        var stepCount = batch.Steps.Distinct().Count();
        return stepCount switch
        {
            1 when batch.Parallel is { } max && max < count => max == 1 ? $"Publish {count} projects, one after another?" : $"Publish {count} projects, {max} at a time?",
            1 => $"Publish {count} projects, all at the same time?",
            _ when stepCount == count => $"Publish {count} projects, one after another?",
            _ => $"Publish {count} projects in {stepCount} steps?"
        };
    }
}
