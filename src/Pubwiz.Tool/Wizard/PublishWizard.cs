using Pubwiz.Core.Discovery;
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
/// </remarks>
public sealed class PublishWizard(
    IAnsiConsole console,
    WorkspacePage workspacePage,
    ProfilePage profilePage,
    ConfirmPage confirmPage,
    PublishRunner runner,
    PasswordProvider passwords,
    AppOfflinePrompt appOffline)
{
    /// <returns>What to publish, or <c>null</c> when the user chose Exit.</returns>
    public async Task<PublishBatch?> RunAsync(WizardOptions options, CancellationToken cancellationToken)
    {
        var workspace = await workspacePage.LoadAsync(options.Path, options.IncludeAll, cancellationToken);

        var candidates = (options.IncludeAll ? workspace.Projects : workspace.Projects.Where(p => p.IsPublishable))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0)
        {
            throw new UserErrorException("No publishable projects were found. Use --all to include libraries and test projects.");
        }

        var presetFilePath = PresetPage.GetPresetFilePath(workspace, options.Path);
        while (true)
        {
            // Read each time the list is shown, so the "x ago" stays right after going back to it.
            var (gitHeader, gitHeaderUpdate) = await GetGitHeaderAsync(options.Git);
            if (await PickProjectsAsync(candidates, gitHeader, gitHeaderUpdate, cancellationToken) is not { } projects)
            {
                return null;
            }

            if (await ChooseProfilesAsync(projects, presetFilePath, options, cancellationToken) is { } batch)
            {
                return batch;
            }

            console.WriteLine();
        }
    }

    /// <summary>
    /// Space-selected projects are published as a batch; pressing Enter without selecting
    /// publishes only the highlighted project.
    /// </summary>
    /// <returns>The projects to publish, or <c>null</c> when the user chose Exit.</returns>
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

    private async Task<IReadOnlyList<ProjectInfo>?> PickProjectsAsync(IReadOnlyList<ProjectInfo> projects, string? header, Task<string?>? headerUpdate,
        CancellationToken cancellationToken)
    {
        var prompt = new MultiSelectPrompt<ProjectInfo>("Which projects do you want to publish?", projects, ProjectColumns)
        {
            IsSelectable = p => p.EvaluationError is null,
            ExitLabel = "Exit",
            Header = header,
            HeaderUpdate = headerUpdate,
            Hint = "Select several projects to publish them as a batch, or press enter to publish only the highlighted one."
        };

        if (await prompt.ShowAsync(console, cancellationToken) is not { } result)
        {
            return null;
        }

        var picked = result.Selected.Count > 0 ? result.Selected : [result.Highlighted];
        console.MarkupLine($"Publishing: [{Widgets.PrimaryColorMarkup}]{string.Join(", ", picked.Select(p => p.Name)).EscapeMarkup()}[/]");
        return picked;
    }

    /// <summary>
    /// Chooses (or creates) a profile for each project, then asks before publishing.
    /// </summary>
    /// <returns>What to publish, or <c>null</c> to go back to the main menu.</returns>
    private async Task<PublishBatch?> ChooseProfilesAsync(IReadOnlyList<ProjectInfo> projects, string presetFilePath, WizardOptions options,
        CancellationToken cancellationToken)
    {
        // Ctrl+C in these pages cancels this token (and the prompt that's open) instead of ending Pubwiz.
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var ctrlC = CtrlC.CancelInstead(cancellation);

        try
        {
            // With one project, its profile list can save a profile as a preset; batches are saved before publishing.
            var profileOptions = new ProfilePageOptions(projects.Count == 1 ? presetFilePath : null);

            var selections = new List<PublishSelection>();
            foreach (var project in projects)
            {
                if (projects.Count > 1)
                {
                    console.Write(new Rule($"[{Widgets.PrimaryColorMarkup}]{project.Name.EscapeMarkup()}[/]").LeftJustified());
                }

                if (await profilePage.ChooseAsync(project, profileOptions, cancellation.Token) is not { } profile)
                {
                    return null;
                }

                selections.Add(new PublishSelection(project, profile));
            }

            var batch = new PublishBatch(selections, PresetFilePath: presetFilePath);
            if (options.Confirm && !confirmPage.Confirm(batch, "Cancel [dim]· back to the projects[/]"))
            {
                return null;
            }

            // Asked here rather than when publishing, so Esc or Ctrl+C in these prompts also goes back to the projects.
            // The answers are remembered, so publishing doesn't ask again.
            foreach (var (project, profile) in batch.Selections)
            {
                if (options.Confirm)
                {
                    appOffline.Ask(project, profile, showProjectName: batch.Selections.Count > 1);
                }

                if (runner.NeedsPassword(profile))
                {
                    await passwords.GetAsync(project, profile, allowPrompt: true, cancellation.Token);
                }
            }

            return batch;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Esc in a list or password prompt (PromptCancelledException), or Ctrl+C.
            CtrlC.RestoreTerminal();
            console.MarkupLine("[dim]Cancelled, back to the projects[/]");
            return null;
        }
    }

    private static IReadOnlyList<string> ProjectColumns(ProjectInfo project) =>
    [
        project.Name,
        project.EvaluationError is null ? project.Kind.DisplayName() : "failed to load",
        string.Join(", ", project.TargetFrameworks),
        project.SolutionFolder ?? string.Empty
    ];
}
