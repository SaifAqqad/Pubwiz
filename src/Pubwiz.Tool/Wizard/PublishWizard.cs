using Pubwiz.Core.Discovery;
using Pubwiz.Core.Presets;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Tool.Credentials;
using Pubwiz.Tool.Infrastructure;
using Pubwiz.Core.Infrastructure.Git;
using Pubwiz.Tool.Rendering;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// The interactive flow: find the workspace, pick projects (the main menu), then choose (or create) a profile for each one.
/// </summary>
/// <remarks>
/// Leaving the profile pages or the password prompts (Esc, Ctrl+C, "Back" or "Cancel") comes back to the main menu,
/// with the projects already loaded. Esc or Ctrl+C in the main menu ends Pubwiz.
/// Started in a project's folder, the main menu is that project's profile list instead, which can show the whole solution.
/// </remarks>
public sealed class PublishWizard(
    IAnsiConsole console,
    WorkspacePage workspacePage,
    ProfilePage profilePage,
    ConfirmPage confirmPage,
    PresetPage presets,
    PublishRunner runner,
    PasswordProvider passwords,
    CertificateTrustPrompt certificates)
{
    /// <summary>Where the main menu leads.</summary>
    private abstract record Next
    {
        public sealed record Publish(PublishBatch Batch) : Next;

        public sealed record ShowSolution : Next;

        public sealed record Exit : Next;
    }

    /// <summary>A row of the project list: a project, or an action above the projects.</summary>
    private abstract record ProjectRow
    {
        public sealed record Project(ProjectInfo Info) : ProjectRow;

        public sealed record PublishAgain(string Description) : ProjectRow;

        public sealed record ShowSolution(string Name) : ProjectRow;
    }

    /// <returns>What to publish, or <c>null</c> when the user chose Exit.</returns>
    public async Task<PublishBatch?> RunAsync(WizardOptions options, CancellationToken cancellationToken)
    {
        var workspace = await workspacePage.LoadAsync(options.Path, options.IncludeAll, cancellationToken, startFolderOnly: true);
        var recent = options.Recent;

        while (true)
        {
            var candidates = (options.IncludeAll ? workspace.Projects : workspace.Projects.Where(p => p.IsPublishable))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (candidates.Count == 0)
            {
                throw new UserErrorException("No publishable projects were found. Use --all to include libraries and test projects.");
            }

            var presetFilePath = PresetPage.GetPresetFilePath(workspace, options.Path);
            var next = workspace.IsStartFolderOnly && candidates.Count == 1
                ? await ChooseForStartProjectAsync(candidates[0], workspace, presetFilePath, options, recent, cancellationToken)
                : await ChooseFromProjectListAsync(candidates, workspace, presetFilePath, options, recent, cancellationToken);

            switch (next)
            {
                case Next.Publish { Batch: var batch }:
                    return batch;

                case Next.ShowSolution:
                    // The last publish was offered already, and belongs to the project's folder rather than the solution.
                    recent = null;
                    console.WriteLine();
                    workspace = await workspacePage.LoadSolutionAsync(workspace, options.IncludeAll, cancellationToken);
                    break;

                default:
                    return null;
            }
        }
    }

    /// <summary>The main menu when started in a project's folder: the project's profile list.</summary>
    private async Task<Next> ChooseForStartProjectAsync(ProjectInfo project, Workspace workspace, string presetFilePath, WizardOptions options,
        RecentRun? recent, CancellationToken cancellationToken)
    {
        if (options.Git is not null && await options.Git.GetLatestAsync() is { } git)
        {
            console.MarkupLine(GitSummary.ToMarkup(git, DateTimeOffset.Now, maxWidth: console.Profile.Width - 1));
            console.WriteLine();
        }

        while (true)
        {
            var pageOptions = new ProfilePageOptions(presetFilePath,
                PublishAgain: recent is null ? null : PresetPage.DescribeRecent(recent, DateTimeOffset.Now),
                ShowSolution: WorkspacePage.DescribeSolution(workspace),
                BackExits: true);

            // Ctrl+C here (or in a page it opens, e.g. creating a profile) ends Pubwiz, as in the project list.
            switch (await profilePage.ChooseAsync(project, pageOptions, cancellationToken))
            {
                case ProfilePageResult.Chosen { Profile: var profile }:
                    var single = new PublishBatch([new PublishSelection(project, profile) { Order = 1 }], Parallel: options.Parallel, PresetFilePath: presetFilePath);
                    if (await FinishAsync(single, options, "the profiles", cancellationToken) is { } batch)
                    {
                        return new Next.Publish(batch);
                    }

                    break;

                case ProfilePageResult.PublishAgain:
                    if (await presets.TryResolveRecentAsync(recent!, presetFilePath, cancellationToken) is not { } again)
                    {
                        recent = null;
                    }
                    else if (await FinishAsync(again, options, "the profiles", cancellationToken) is { } confirmed)
                    {
                        return new Next.Publish(confirmed);
                    }

                    break;

                case ProfilePageResult.ShowSolution:
                    return new Next.ShowSolution();

                default:
                    return new Next.Exit();
            }

            console.WriteLine();
        }
    }

    /// <summary>The main menu: the project list, with the last publish and (when started in a project's folder) the solution above it.</summary>
    private async Task<Next> ChooseFromProjectListAsync(IReadOnlyList<ProjectInfo> candidates, Workspace workspace, string presetFilePath,
        WizardOptions options, RecentRun? recent, CancellationToken cancellationToken)
    {
        while (true)
        {
            // Read each time the list is shown, so the "x ago" stays right after going back to it.
            var (gitHeader, gitHeaderUpdate) = await GetGitHeaderAsync(options.Git);
            List<ProjectRow> actions = [];
            if (recent is not null)
            {
                actions.Add(new ProjectRow.PublishAgain(PresetPage.DescribeRecent(recent, DateTimeOffset.Now)));
            }

            if (workspace.IsStartFolderOnly)
            {
                actions.Add(new ProjectRow.ShowSolution(WorkspacePage.DescribeSolution(workspace)));
            }

            PublishBatch? batch = null;
            switch (await PickProjectsAsync(candidates, actions, gitHeader, gitHeaderUpdate, cancellationToken))
            {
                case null:
                    return new Next.Exit();

                case [ProjectRow.ShowSolution]:
                    return new Next.ShowSolution();

                case [ProjectRow.PublishAgain]:
                    if (await presets.TryResolveRecentAsync(recent!, presetFilePath, cancellationToken) is not { } again)
                    {
                        recent = null;
                        break;
                    }

                    batch = await FinishAsync(again, options, "the projects", cancellationToken);
                    break;

                case var rows:
                    var projects = rows.OfType<ProjectRow.Project>().Select(row => row.Info).ToList();
                    batch = await ChooseProfilesAsync(projects, presetFilePath, options, cancellationToken);
                    break;
            }

            if (batch is not null)
            {
                return new Next.Publish(batch);
            }

            console.WriteLine();
        }
    }

    /// <summary>
    /// The git line for the project list: what's known now, and (while the background fetch runs) the line to show once
    /// it's done, so the list never waits for the network.
    /// </summary>
    private async Task<(string? Header, Task<string?>? Update)> GetGitHeaderAsync(GitStatus? git)
    {
        if (git is null || await git.GetLatestAsync() is not { } now)
        {
            return (null, null);
        }

        return (ToMarkup(now), git.PendingFetch is { } pending ? ToMarkupAsync(pending) : null);

        string ToMarkup(GitInfo info) => GitSummary.ToMarkup(info, DateTimeOffset.Now, maxWidth: console.Profile.Width - 1);
        async Task<string?> ToMarkupAsync(Task<GitInfo?> fetch) => await fetch is { } fetched ? ToMarkup(fetched) : null;
    }

    /// <summary>
    /// Space-selected projects are published as a batch; pressing Enter without selecting publishes only the
    /// highlighted project, or runs the highlighted action.
    /// </summary>
    /// <param name="actions">Rows shown above the projects.</param>
    /// <returns>The chosen projects, a single action, or <c>null</c> when the user chose Exit.</returns>
    private async Task<IReadOnlyList<ProjectRow>?> PickProjectsAsync(IReadOnlyList<ProjectInfo> projects, IReadOnlyList<ProjectRow> actions,
        string? header, Task<string?>? headerUpdate, CancellationToken cancellationToken)
    {
        List<ProjectRow> rows = [.. actions, .. projects.Select(p => new ProjectRow.Project(p))];
        var prompt = new MultiSelectPrompt<ProjectRow>("Which projects do you want to publish?", rows, RowColumns)
        {
            IsSelectable = row => row is ProjectRow.Project { Info.EvaluationError: null },
            IsAction = row => row is not ProjectRow.Project,
            ExitLabel = "Exit",
            Header = header,
            HeaderUpdate = headerUpdate,
            Hint = "Select several projects to publish them as a batch, or press enter to publish only the highlighted one."
        };

        if (await prompt.ShowAsync(console, cancellationToken) is not { } result)
        {
            return null;
        }

        if (result.Selected.Count == 0 && result.Highlighted is not ProjectRow.Project)
        {
            return [result.Highlighted];
        }

        var picked = result.Selected.Count > 0 ? result.Selected : [result.Highlighted];
        var names = picked.OfType<ProjectRow.Project>().Select(p => p.Info.Name);
        console.MarkupLine($"Publishing: [{Widgets.PrimaryColorMarkup}]{string.Join(", ", names).EscapeMarkup()}[/]");
        return picked;
    }

    /// <summary>
    /// Chooses (or creates) a profile for each project, then asks before publishing.
    /// </summary>
    /// <returns>What to publish, or <c>null</c> to go back to the main menu.</returns>
    private Task<PublishBatch?> ChooseProfilesAsync(IReadOnlyList<ProjectInfo> projects, string presetFilePath, WizardOptions options,
        CancellationToken cancellationToken) =>
        CancellableAsync(async token =>
        {
            if (await ChooseProfileForEachAsync(projects, presetFilePath, token) is not { } selections)
            {
                return null;
            }

            // Everything at the same time to start with; the order list changes that.
            var batch = new PublishBatch(selections.Select(s => s with { Order = 1 }).ToList(), Parallel: options.Parallel, PresetFilePath: presetFilePath);
            if (selections.Count > 1)
            {
                batch = confirmPage.ChooseOrder(batch);
            }

            return await ConfirmAndAskAsync(batch, options, CancelLabel("the projects"), token);
        }, "the projects", cancellationToken);

    /// <summary>Asks before publishing a batch whose profiles are chosen already (one project, or the last publish).</summary>
    /// <param name="backTo">The main menu, for the cancel option, e.g. "the projects".</param>
    /// <returns>What to publish, or <c>null</c> to go back to the main menu.</returns>
    private Task<PublishBatch?> FinishAsync(PublishBatch batch, WizardOptions options, string backTo, CancellationToken cancellationToken) =>
        CancellableAsync(token => ConfirmAndAskAsync(batch, options, CancelLabel(backTo), token), backTo, cancellationToken);

    private static string CancelLabel(string backTo) => $"Cancel [dim]· back to {backTo.EscapeMarkup()}[/]";

    /// <summary>
    /// Runs a page where Ctrl+C (and Esc in a list or password prompt) goes back to the main menu instead of ending Pubwiz.
    /// </summary>
    private async Task<PublishBatch?> CancellableAsync(Func<CancellationToken, Task<PublishBatch?>> page, string backTo, CancellationToken cancellationToken)
    {
        // Ctrl+C in these pages cancels this token (and the prompt that's open) instead of ending Pubwiz.
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var ctrlC = CtrlC.CancelInstead(cancellation);

        try
        {
            return await page(cancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Esc in a list or password prompt (PromptCancelledException), or Ctrl+C.
            CtrlC.RestoreTerminal();
            console.MarkupLine($"[dim]Cancelled, back to {backTo.EscapeMarkup()}[/]");
            return null;
        }
    }

    /// <summary>The confirm question (unless --yes), then the certificate and password questions.</summary>
    private async Task<PublishBatch?> ConfirmAndAskAsync(PublishBatch batch, WizardOptions options, string cancelLabel, CancellationToken cancellationToken)
    {
        if (options.Confirm)
        {
            if (confirmPage.Confirm(batch, cancelLabel) is not { } confirmed)
            {
                return null;
            }

            batch = confirmed;
        }

        // Asked here rather than when publishing, so Esc or Ctrl+C in these prompts also goes back to the projects.
        // The answers are remembered, so publishing doesn't ask again.
        foreach (var (project, profile) in batch.Selections)
        {
            if (options.Confirm)
            {
                await certificates.AskAsync(profile, cancellationToken);
            }

            if (runner.NeedsPassword(profile))
            {
                var prompt = options.Confirm ? PasswordPrompt.Always : PasswordPrompt.WhenMissing;
                await passwords.GetAsync(project, profile, prompt, cancellationToken);
            }
        }

        return batch;
    }

    /// <summary>
    /// Asks for each project's profile in turn. From the second project on, the list can go back to the previous
    /// project (e.g. after a misclick), which starts on the profile picked there before.
    /// </summary>
    /// <returns>A profile per project, or <c>null</c> when the user went back to the project list.</returns>
    private async Task<IReadOnlyList<PublishSelection>?> ChooseProfileForEachAsync(IReadOnlyList<ProjectInfo> projects, string presetFilePath,
        CancellationToken cancellationToken)
    {
        var chosen = new PublishProfile?[projects.Count];
        var index = 0;
        while (index < projects.Count)
        {
            var project = projects[index];
            if (projects.Count > 1)
            {
                console.Write(new Rule($"[{Widgets.PrimaryColorMarkup}]{project.Name.EscapeMarkup()}[/] [dim]({index + 1}/{projects.Count})[/]").LeftJustified());
            }

            // With one project, its profile list can save a profile as a preset; batches are saved before publishing.
            var options = new ProfilePageOptions(
                PresetFilePath: projects.Count == 1 ? presetFilePath : null,
                PreviousProject: index > 0 ? projects[index - 1].Name : null,
                Selected: chosen[index]);

            switch (await profilePage.ChooseAsync(project, options, cancellationToken))
            {
                case ProfilePageResult.Chosen { Profile: var profile }:
                    chosen[index++] = profile;
                    break;

                case ProfilePageResult.PreviousProject:
                    index--;
                    break;

                default:
                    return null;
            }
        }

        return projects.Select((project, i) => new PublishSelection(project, chosen[i]!)).ToList();
    }

    private static IReadOnlyList<string> RowColumns(ProjectRow row) => row switch
    {
        ProjectRow.Project { Info: var project } =>
        [
            project.Name,
            project.EvaluationError is null ? project.Kind.DisplayName() : "failed to load",
            string.Join(", ", project.TargetFrameworks),
            project.SolutionFolder ?? string.Empty
        ],
        ProjectRow.PublishAgain again => ["↻ Repeat last publish", again.Description, "", ""],
        ProjectRow.ShowSolution solution => [$"≡ Show all projects in {solution.Name}", "", "", ""],
        _ => throw new ArgumentOutOfRangeException(nameof(row))
    };
}
