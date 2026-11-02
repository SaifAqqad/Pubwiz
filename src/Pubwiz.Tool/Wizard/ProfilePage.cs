using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets.WebDeploy;

namespace Pubwiz.Tool.Wizard;

/// <param name="PresetFilePath">Where "Save a profile as a preset" saves to; <c>null</c> hides that option.</param>
public sealed record ProfilePageOptions(string? PresetFilePath);

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

        /// <summary>Back to the project list; Esc picks it too.</summary>
        public sealed record Back : Choice;
    }

    /// <returns>The profile to publish with, or <c>null</c> when the user went back.</returns>
    public async Task<PublishProfile?> ChooseAsync(ProjectInfo project, ProfilePageOptions options, CancellationToken cancellationToken)
    {
        while (true)
        {
            var profiles = PublishProfileStore.List(project.Directory);
            var canSavePreset = options.PresetFilePath is not null && profiles.Count > 0;
            var choice = console.Prompt(BuildPrompt(project, profiles, canSavePreset));

            switch (choice)
            {
                case Choice.Existing { Profile: var profile } when WebDeployAlternatives.AreNeeded(profile, servers):
                    if (await createPage.OfferWebDeployAlternativeAsync(project, profile, cancellationToken) is { } chosen)
                    {
                        return chosen;
                    }

                    break;

                case Choice.Existing existing:
                    return existing.Profile;

                case Choice.Create:
                    if (await createPage.CreateAsync(project, cancellationToken) is { } created)
                    {
                        return created;
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
                        return imported[0];
                    }

                    // Several (or no) profiles imported: show the updated list so the user can pick.
                    break;

                case Choice.SaveAsPreset:
                    SaveAsPreset(project, profiles, options.PresetFilePath!);
                    break;

                case Choice.Back:
                    return null;
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

    private static SelectionPrompt<Choice> BuildPrompt(ProjectInfo project, IReadOnlyList<PublishProfile> profiles, bool canSavePreset)
    {
        var title = profiles.Count > 0
            ? $"Choose a publish profile for [{Widgets.PrimaryColorMarkup}]{project.Name.EscapeMarkup()}[/]:"
            : $"[{Widgets.PrimaryColorMarkup}]{project.Name.EscapeMarkup()}[/] has no publish profiles yet:";

        var prompt = Questions.Menu<Choice>(title)
            .AddChoices(profiles.Select(p => new Choice.Existing(p)))
            .AddChoices(new Choice.Create(), new Choice.Import())
            .AddCancelResult(new Choice.Back())
            .UseConverter(Describe);

        if (canSavePreset)
        {
            prompt.AddChoice(new Choice.SaveAsPreset());
        }

        prompt.AddChoice(new Choice.Back());
        return prompt;
    }

    private static string Describe(Choice choice) => choice switch
    {
        Choice.Existing existing => $"{existing.Profile.Name.EscapeMarkup()} [dim]({existing.Profile.Method})[/]",
        Choice.Create => "[green]+[/] Create a new profile",
        Choice.Import => "[blue]↓[/] Import a profile (.publishsettings or .pubxml)",
        Choice.SaveAsPreset => "[yellow]★[/] Save a profile as a preset",
        Choice.Back => "[dim]← Back to the projects[/]",
        _ => throw new ArgumentOutOfRangeException(nameof(choice))
    };
}
