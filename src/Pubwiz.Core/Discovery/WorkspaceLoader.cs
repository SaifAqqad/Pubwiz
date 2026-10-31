namespace Pubwiz.Core.Discovery;

/// <summary>
/// A loaded workspace: the projects to choose from and where they came from.
/// </summary>
/// <param name="SolutionPath">The solution the projects were read from, or <c>null</c> when loose project files were found.</param>
/// <param name="Projects">All projects, including ones that aren't publishable.</param>
public sealed record Workspace(string? SolutionPath, IReadOnlyList<ProjectInfo> Projects);

/// <summary>
/// Turns a scan result into a <see cref="Workspace"/> by reading the solution (if any) and inspecting its projects.
/// </summary>
public sealed class WorkspaceLoader(ProjectInspector inspector)
{
    /// <param name="solutionPath">The solution to load, or <c>null</c> to load <paramref name="projectPaths"/> directly.</param>
    /// <param name="projectPaths">Loose project files, used when <paramref name="solutionPath"/> is <c>null</c>.</param>
    public async Task<Workspace> LoadAsync(string? solutionPath, IReadOnlyList<string> projectPaths, CancellationToken cancellationToken = default)
    {
        var projects = solutionPath is not null
            ? await SolutionReader.ReadProjectsAsync(solutionPath, cancellationToken)
            : projectPaths.Select(path => new SolutionProject(path, SolutionFolder: null)).ToList();

        var inspected = await inspector.InspectAsync(projects, cancellationToken);
        return new Workspace(solutionPath, inspected);
    }
}
