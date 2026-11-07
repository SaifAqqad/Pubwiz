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
    /// <param name="startFolderOnly">
    /// Started in a project's folder: load only that folder's projects (the solution's can be loaded later with
    /// <see cref="LoadSolutionAsync"/>), so the wizard can start with them.
    /// </param>
    public async Task<Workspace> LoadAsync(string path, bool includeAll, CancellationToken cancellationToken, bool startFolderOnly = false)
    {
        var scan = await ScanAsync(path, cancellationToken);
        if (startFolderOnly && scan.StartProjects.Count > 0)
        {
            var projects = await LoadProjectsAsync(null, scan.StartProjects, cancellationToken);

            // Only this folder's projects are publishable here (e.g. a library's folder): show the whole solution instead.
            if (includeAll || projects.Any(p => p.IsPublishable))
            {
                var focused = new Workspace(scan.SolutionPaths.Count == 1 ? scan.SolutionPaths[0] : null, projects, scan.SolutionPaths);
                WriteSummary(focused, includeAll);
                return focused;
            }
        }

        return await LoadSolutionAsync(scan.SolutionPaths, scan.ProjectPaths, includeAll, cancellationToken);
    }

    /// <summary>Loads every project of the solution a start-folder-only workspace belongs to.</summary>
    public Task<Workspace> LoadSolutionAsync(Workspace startFolder, bool includeAll, CancellationToken cancellationToken) =>
        LoadSolutionAsync(startFolder.ParentSolutionPaths ?? [], [], includeAll, cancellationToken);

    /// <summary>The name of the solution a start-folder-only workspace belongs to, e.g. <c>Shop.slnx</c> (or "the solution" when there are several).</summary>
    public static string DescribeSolution(Workspace startFolder) =>
        startFolder.ParentSolutionPaths is [var only] ? Path.GetFileName(only) : "the solution";

    private async Task<Workspace> LoadSolutionAsync(IReadOnlyList<string> solutionPaths, IReadOnlyList<string> projectPaths, bool includeAll,
        CancellationToken cancellationToken)
    {
        var solutionPath = solutionPaths.Count switch
        {
            0 => null,
            1 => solutionPaths[0],
            _ => ChooseSolution(solutionPaths)
        };

        var workspace = new Workspace(solutionPath, await LoadProjectsAsync(solutionPath, projectPaths, cancellationToken));
        WriteSummary(workspace, includeAll);
        return workspace;
    }

    private async Task<IReadOnlyList<ProjectInfo>> LoadProjectsAsync(string? solutionPath, IReadOnlyList<string> projectPaths, CancellationToken cancellationToken)
    {
        var workspace = await console.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(new Style(Widgets.PrimaryColor))
            .StartAsync("Loading projects...", _ => loader.LoadAsync(solutionPath, projectPaths, cancellationToken));
        return workspace.Projects;
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
        if (workspace.IsStartFolderOnly)
        {
            var solutions = string.Join(", ", workspace.ParentSolutionPaths!.Select(s => DisplayText.Link(s, Path.GetRelativePath(Directory.GetCurrentDirectory(), s))));
            var names = string.Join(", ", workspace.Projects.Select(p => $"[{Widgets.PrimaryColorMarkup}]{p.Name.EscapeMarkup()}[/]"));
            console.MarkupLine($"Project {names} [dim]· part of[/] {solutions}");
            WriteLoadErrors(workspace);
            console.WriteLine();
            return;
        }

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

        WriteLoadErrors(workspace);
        console.WriteLine();
    }

    private void WriteLoadErrors(Workspace workspace)
    {
        foreach (var failed in workspace.Projects.Where(p => p.EvaluationError is not null))
        {
            console.Write(Widgets.Warning($"{failed.Name} could not be loaded: {failed.EvaluationError}"));
            console.WriteLine();
        }
    }
}
