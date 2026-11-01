using Pubwiz.Core.Discovery;
using Pubwiz.Tool.Rendering;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Finds the solution or projects to work with and loads them.
/// </summary>
public sealed class WorkspacePage(IAnsiConsole console, WorkspaceLoader loader)
{
    /// <param name="path">Folder, solution or project to start from.</param>
    /// <param name="includeAll">Count every project (--all), not only the publishable ones.</param>
    /// <param name="cancellationToken">Cancels loading.</param>
    public async Task<Workspace> LoadAsync(string path, bool includeAll, CancellationToken cancellationToken)
    {
        var scan = await ScanAsync(path, cancellationToken);
        var solutionPath = scan.SolutionPaths.Count switch
        {
            0 => null,
            1 => scan.SolutionPaths[0],
            _ => ChooseSolution(scan.SolutionPaths)
        };

        var workspace = await console.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(new Style(Widgets.PrimaryColor))
            .StartAsync("Loading projects...", _ => loader.LoadAsync(solutionPath, scan.ProjectPaths, cancellationToken));

        WriteSummary(workspace, includeAll);
        return workspace;
    }

    private static async Task<WorkspaceScanResult> ScanAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var result = await WorkspaceScanner.ScanAsync(path, cancellationToken);
            return result.IsEmpty
                ? throw new UserErrorException($"No solution or project was found in '{path}' (searched its parent folders, and {WorkspaceScanner.MaxDepth} folders deep).")
                : result;
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or ArgumentException)
        {
            throw new UserErrorException(ex.Message);
        }
    }

    private string ChooseSolution(IReadOnlyList<string> solutions)
    {
        var baseFolder = Directory.GetCurrentDirectory();
        return console.Prompt(Questions.Menu<string>("Several solutions were found. Which one do you want to use?")
            .AddChoices(solutions)
            .UseConverter(solution => Path.GetRelativePath(baseFolder, solution).EscapeMarkup()));
    }

    private void WriteSummary(Workspace workspace, bool includeAll)
    {
        // A solution found in a parent folder is shown as ../App.sln, so it's clear where it came from.
        var source = workspace.SolutionPath is { } solution
            ? DisplayText.Link(solution, Path.GetRelativePath(Directory.GetCurrentDirectory(), solution))
            : "the current folder";
        // Count what the project list will show: libraries, tests and the like are left out unless --all is used.
        var publishable = workspace.Projects.Count(p => p.IsPublishable);
        var hidden = workspace.Projects.Count - publishable;
        if (includeAll || hidden == 0)
        {
            console.MarkupLine($"Found [{Widgets.PrimaryColorMarkup}]{workspace.Projects.Count}[/] project(s) in {source}");
        }
        else
        {
            console.MarkupLine($"Found [{Widgets.PrimaryColorMarkup}]{publishable}[/] publishable project(s) in {source} " +
                               $"[dim]· {hidden} other(s) hidden, --all shows them[/]");
        }

        foreach (var failed in workspace.Projects.Where(p => p.EvaluationError is not null))
        {
            console.Write(Widgets.Warning($"{failed.Name} could not be loaded: {failed.EvaluationError}"));
            console.WriteLine();
        }

        console.WriteLine();
    }
}
