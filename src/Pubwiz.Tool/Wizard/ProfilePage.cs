using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets.WebDeploy;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Lets the user pick one of a project's publish profiles, or create/import a new one.
/// </summary>
public sealed class ProfilePage(IAnsiConsole console, CreateProfilePage createPage, ImportProfilePage importPage)
{
    /// <summary>The options in the profile menu.</summary>
    private abstract record Choice
    {
        public sealed record Existing(PublishProfile Profile) : Choice;

        public sealed record Create : Choice;

        public sealed record Import : Choice;
    }

    public async Task<PublishProfile> ChooseAsync(ProjectInfo project, CancellationToken cancellationToken)
    {
        while (true)
        {
            var profiles = PublishProfileStore.List(project.Directory);
            var choice = console.Prompt(BuildPrompt(project, profiles));

            switch (choice)
            {
                case Choice.Existing { Profile: var profile } when WebDeployAlternatives.AreNeeded(profile):
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
                    var imported = await importPage.ImportAsync(project, cancellationToken);
                    if (imported.Count == 1)
                    {
                        return imported[0];
                    }

                    // Several (or no) profiles imported: show the updated list so the user can pick.
                    break;
            }
        }
    }

    private static SelectionPrompt<Choice> BuildPrompt(ProjectInfo project, IReadOnlyList<PublishProfile> profiles)
    {
        var title = profiles.Count > 0
            ? $"Choose a publish profile for [{Widgets.PrimaryColorMarkup}]{project.Name.EscapeMarkup()}[/]:"
            : $"[{Widgets.PrimaryColorMarkup}]{project.Name.EscapeMarkup()}[/] has no publish profiles yet:";

        return new SelectionPrompt<Choice>()
            .Title(title)
            .HighlightStyle(new Style(Widgets.PrimaryColor))
            .AddChoices(profiles.Select(p => new Choice.Existing(p)))
            .AddChoices(new Choice.Create(), new Choice.Import())
            .UseConverter(Describe);
    }

    private static string Describe(Choice choice) => choice switch
    {
        Choice.Existing existing => $"{existing.Profile.Name.EscapeMarkup()} [dim]({existing.Profile.Method})[/]",
        Choice.Create => "[green]+[/] Create a new profile",
        Choice.Import => "[blue]↓[/] Import a profile (.publishsettings or .pubxml)",
        _ => throw new ArgumentOutOfRangeException(nameof(choice))
    };
}
