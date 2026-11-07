using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets.ServerLogins;
using Pubwiz.Core.Targets.WebDeploy;

namespace Pubwiz.Tool.Wizard;

/// <param name="PresetFilePath">Where "Save a profile as a preset" saves to; <c>null</c> hides that option.</param>
/// <param name="PreviousProject">In a batch, the project before this one, offered as "Back to …"; <c>null</c> for the first.</param>
/// <param name="Selected">The profile picked for this project earlier (after going back to it), highlighted in the list.</param>
/// <param name="PublishAgain">The last publish from here, e.g. "Shop.Api → Production · 2 hours ago", offered at the top; <c>null</c> hides it.</param>
/// <param name="ShowSolution">The solution whose projects can be shown instead, e.g. "Shop.slnx"; <c>null</c> hides that option.</param>
/// <param name="BackExits">There's no project list to go back to: "Back" becomes "Exit".</param>
public sealed record ProfilePageOptions(string? PresetFilePath, string? PreviousProject = null, PublishProfile? Selected = null,
    string? PublishAgain = null, string? ShowSolution = null, bool BackExits = false);

/// <summary>What the user did in a project's profile list.</summary>
public abstract record ProfilePageResult
{
    /// <summary>Publish the project with this profile.</summary>
    public sealed record Chosen(PublishProfile Profile) : ProfilePageResult;

    /// <summary>Back to the previous project in the batch, to change its profile.</summary>
    public sealed record PreviousProject : ProfilePageResult;

    /// <summary>Back to the project list (or exit, see <see cref="ProfilePageOptions.BackExits"/>).</summary>
    public sealed record BackToProjects : ProfilePageResult;

    /// <summary>Publish what was published last time from here.</summary>
    public sealed record PublishAgain : ProfilePageResult;

    /// <summary>Show the projects of the solution this project belongs to.</summary>
    public sealed record ShowSolution : ProfilePageResult;
}

/// <summary>
/// Lets the user pick one of a project's publish profiles, or create/import a new one.
/// </summary>
public sealed class ProfilePage(IAnsiConsole console, CreateProfilePage createPage, ImportProfilePage importPage, PresetPage presetPage,
    ServerCredentialStore servers)
{
    /// <summary>The options in the profile menu.</summary>
    private abstract record Choice
    {
        public sealed record Existing(PublishProfile Profile) : Choice;

        public sealed record Create : Choice;

        public sealed record Import : Choice;

        public sealed record SaveAsPreset : Choice;

        public sealed record PublishAgain(string Description) : Choice;

        public sealed record ShowSolution(string Name) : Choice;

        /// <summary>Back to the previous project in a batch.</summary>
        public sealed record PreviousProject(string Name) : Choice;

        /// <summary>Back to the project list (or exit); Esc picks it too.</summary>
        public sealed record Back(bool Exits) : Choice;
    }

    public async Task<ProfilePageResult> ChooseAsync(ProjectInfo project, ProfilePageOptions options, CancellationToken cancellationToken)
    {
        var chosen = await ChooseProfileAsync(project, options, cancellationToken);
        if (chosen is ProfilePageResult.Chosen { Profile: var profile })
        {
            // The list disappears once answered, so the answer is written the way other answers look.
            console.MarkupLine($"[bold]Profile:[/] [{Widgets.PrimaryColorMarkup}]{profile.Name.EscapeMarkup()}[/] [dim]({profile.Method})[/]");
        }

        return chosen;
    }

    private async Task<ProfilePageResult> ChooseProfileAsync(ProjectInfo project, ProfilePageOptions options, CancellationToken cancellationToken)
    {
        while (true)
        {
            var profiles = PublishProfileStore.List(project.Directory);
            var canSavePreset = options.PresetFilePath is not null && profiles.Count > 0;
            var choice = console.Prompt(BuildPrompt(project, profiles, canSavePreset, options));

            switch (choice)
            {
                case Choice.Existing { Profile: var profile } when WebDeployAlternatives.AreNeeded(profile, servers):
                    if (await createPage.OfferWebDeployAlternativeAsync(project, profile, cancellationToken) is { } alternative)
                    {
                        return new ProfilePageResult.Chosen(alternative);
                    }

                    break;

                case Choice.Existing existing:
                    return new ProfilePageResult.Chosen(existing.Profile);

                case Choice.Create:
                    if (await createPage.CreateAsync(project, cancellationToken) is { } created)
                    {
                        return new ProfilePageResult.Chosen(created);
                    }

                    break;

                case Choice.Import:
                    IReadOnlyList<PublishProfile> imported;
                    try
                    {
                        imported = await importPage.ImportAsync(project, cancellationToken);
                    }
                    catch (UserErrorException ex)
                    {
                        // e.g. a file with no supported profiles: say so and show the profile list again.
                        console.Write(Widgets.Error(ex.Message));
                        console.WriteLine();
                        break;
                    }

                    if (imported.Count == 1)
                    {
                        return new ProfilePageResult.Chosen(imported[0]);
                    }

                    // Several (or no) profiles imported: show the updated list so the user can pick.
                    break;

                case Choice.SaveAsPreset:
                    SaveAsPreset(project, profiles, options.PresetFilePath!);
                    break;

                case Choice.PreviousProject:
                    return new ProfilePageResult.PreviousProject();

                case Choice.PublishAgain:
                    return new ProfilePageResult.PublishAgain();

                case Choice.ShowSolution:
                    return new ProfilePageResult.ShowSolution();

                case Choice.Back:
                    return new ProfilePageResult.BackToProjects();
            }
        }
    }

    /// <summary>Saves one of the project's profiles as a preset, so it can be published with <c>--preset</c>.</summary>
    private void SaveAsPreset(ProjectInfo project, IReadOnlyList<PublishProfile> profiles, string presetFilePath)
    {
        var profile = profiles.Count == 1 ? profiles[0] : console.Choose("Profile to save as a preset", profiles, p => p.Name.EscapeMarkup());
        presetPage.Save([new PublishSelection(project, profile)], presetFilePath, suggestedName: profile.Name);
        console.WriteLine();
    }

    private static SelectionPrompt<Choice> BuildPrompt(ProjectInfo project, IReadOnlyList<PublishProfile> profiles, bool canSavePreset, ProfilePageOptions options)
    {
        var title = profiles.Count > 0
            ? $"Choose a publish profile for [{Widgets.PrimaryColorMarkup}]{project.Name.EscapeMarkup()}[/]:"
            : $"[{Widgets.PrimaryColorMarkup}]{project.Name.EscapeMarkup()}[/] has no publish profiles yet:";

        var existing = profiles.Select(p => new Choice.Existing(p)).ToList();
        var back = new Choice.Back(options.BackExits);
        var prompt = Questions.Menu<Choice>(title).UseConverter(Describe);

        // First, so it's the default: publishing the same as last time is one key press.
        if (options.PublishAgain is { } again)
        {
            prompt.AddChoice(new Choice.PublishAgain(again));
        }

        prompt.AddChoices(existing)
            .AddChoices(new Choice.Create(), new Choice.Import())
            .AddCancelResult(back);

        // Coming back to this project: start on the profile picked before (the list is read again, so match by file).
        if (existing.FirstOrDefault(c => options.Selected is { FilePath: { } path } && c.Profile.FilePath == path) is { } selected)
        {
            prompt.DefaultValue(selected);
        }

        if (canSavePreset)
        {
            prompt.AddChoice(new Choice.SaveAsPreset());
        }

        if (options.ShowSolution is { } solution)
        {
            prompt.AddChoice(new Choice.ShowSolution(solution));
        }

        if (options.PreviousProject is { } previous)
        {
            prompt.AddChoice(new Choice.PreviousProject(previous));
        }

        prompt.AddChoice(back);
        return prompt;
    }

    private static string Describe(Choice choice) => choice switch
    {
        Choice.Existing existing => $"{existing.Profile.Name.EscapeMarkup()} [dim]({existing.Profile.Method})[/]",
        Choice.Create => "[green]+[/] Create a new profile",
        Choice.Import => "[blue]↓[/] Import a profile (.publishsettings or .pubxml)",
        Choice.SaveAsPreset => "[yellow]★[/] Save a profile as a preset",
        Choice.PublishAgain again => $"[{Widgets.PrimaryColorMarkup}]↻ Repeat last publish[/] [dim]{again.Description.EscapeMarkup()}[/]",
        Choice.ShowSolution solution => $"[blue]≡[/] Show all projects in {solution.Name.EscapeMarkup()}",
        Choice.PreviousProject previous => $"[dim]← Back to {previous.Name.EscapeMarkup()} (the previous project)[/]",
        Choice.Back { Exits: true } => "[dim]✕ Exit[/]",
        Choice.Back => "[dim]← Back to the projects[/]",
        _ => throw new ArgumentOutOfRangeException(nameof(choice))
    };
}
