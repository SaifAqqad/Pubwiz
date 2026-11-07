namespace Pubwiz.Core.Discovery;

/// <summary>
/// What <see cref="WorkspaceScanner"/> found.
/// </summary>
/// <param name="SolutionPaths">Solution files (.sln/.slnx) found at the first level that had any.</param>
/// <param name="ProjectPaths">Project files found at the first level that had any (only when no solution was found).</param>
/// <param name="StartProjectPaths">
/// When the search started in a project's own folder and found a parent solution that includes it: the project files in
/// that folder, so the wizard can start with them before showing the whole solution. Empty otherwise.
/// </param>
public sealed record WorkspaceScanResult(IReadOnlyList<string> SolutionPaths, IReadOnlyList<string> ProjectPaths, IReadOnlyList<string>? StartProjectPaths = null)
{
    public static WorkspaceScanResult Empty { get; } = new([], []);

    /// <summary>See the <c>StartProjectPaths</c> parameter; never <c>null</c>.</summary>
    public IReadOnlyList<string> StartProjects => StartProjectPaths ?? [];

    public bool IsEmpty => SolutionPaths.Count == 0 && ProjectPaths.Count == 0;
}
