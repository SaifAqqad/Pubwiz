using Pubwiz.Core.Discovery;
using Pubwiz.Tool.Prompts;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// The interactive flow: find the workspace, pick projects, then choose (or create) a profile for each one.
/// </summary>
public sealed class PublishWizard(IAnsiConsole console, WorkspacePage workspacePage, ProfilePage profilePage)
{
    public async Task<IReadOnlyList<PublishSelection>> RunAsync(WizardOptions options, CancellationToken cancellationToken)
    {
        var workspace = await workspacePage.LoadAsync(options.Path, cancellationToken);

        var candidates = options.IncludeAll ? workspace.Projects : workspace.Projects.Where(p => p.IsPublishable).ToList();
        if (candidates.Count == 0)
        {
            throw new UserErrorException("No publishable projects were found. Use --all to include libraries and test projects.");
        }

        var projects = candidates.Count == 1 ? candidates : await PickProjectsAsync(candidates, cancellationToken);

        var selections = new List<PublishSelection>();
        foreach (var project in projects)
        {
            if (projects.Count > 1)
            {
                console.Write(new Rule($"[{Widgets.PrimaryColorMarkup}]{project.Name.EscapeMarkup()}[/]").LeftJustified());
            }

            var profile = await profilePage.ChooseAsync(project, cancellationToken);
            selections.Add(new PublishSelection(project, profile));
        }

        return selections;
    }

    /// <summary>
    /// Space-selected projects are published as a batch; pressing Enter without selecting
    /// publishes only the highlighted project.
    /// </summary>
    private async Task<IReadOnlyList<ProjectInfo>> PickProjectsAsync(IReadOnlyList<ProjectInfo> projects, CancellationToken cancellationToken)
    {
        var prompt = new MultiSelectPrompt<ProjectInfo>("Which projects do you want to publish?", projects, ProjectColumns)
        {
            IsSelectable = p => p.EvaluationError is null,
            Hint = "Select several projects to publish them as a batch, or press enter to publish only the highlighted one."
        };

        var result = await prompt.ShowAsync(console, cancellationToken);
        var picked = result.Selected.Count > 0 ? result.Selected : [result.Highlighted];

        console.MarkupLine($"Publishing: [{Widgets.PrimaryColorMarkup}]{string.Join(", ", picked.Select(p => p.Name)).EscapeMarkup()}[/]");
        return picked;
    }

    private static IReadOnlyList<string> ProjectColumns(ProjectInfo project) =>
    [
        project.Name,
        project.EvaluationError is null ? project.Kind.DisplayName() : "failed to load",
        string.Join(", ", project.TargetFrameworks),
        project.SolutionFolder ?? string.Empty
    ];
}
