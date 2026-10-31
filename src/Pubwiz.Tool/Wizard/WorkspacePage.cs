using Pubwiz.Core.Discovery;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Finds the solution or projects to work with and loads them.
/// </summary>
public sealed class WorkspacePage(IAnsiConsole console, WorkspaceLoader loader)
{
    public async Task<Workspace> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var scan = Scan(path);
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

        WriteSummary(workspace);
        return workspace;
    }

    private static WorkspaceScanResult Scan(string path)
    {
        try
        {
            var result = WorkspaceScanner.Scan(path);
            return result.IsEmpty
                ? throw new UserErrorException($"No solution or project was found in '{path}' (searched {WorkspaceScanner.MaxDepth} folders deep).")
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

    private void WriteSummary(Workspace workspace)
    {
        var source = workspace.SolutionPath is null ? "the current folder" : Path.GetFileName(workspace.SolutionPath);
        console.MarkupLine($"Found [{Widgets.PrimaryColorMarkup}]{workspace.Projects.Count}[/] project(s) in {source.EscapeMarkup()}");

        foreach (var failed in workspace.Projects.Where(p => p.EvaluationError is not null))
        {
            console.Write(Widgets.Warning($"{failed.Name} could not be loaded: {failed.EvaluationError}"));
            console.WriteLine();
        }

        console.WriteLine();
    }
}
