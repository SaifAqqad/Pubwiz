namespace Pubwiz.Core.Discovery;

/// <summary>
/// What <see cref="WorkspaceScanner"/> found.
/// </summary>
/// <param name="SolutionPaths">Solution files (.sln/.slnx) found at the first level that had any.</param>
/// <param name="ProjectPaths">Project files found at the first level that had any (only when no solution was found).</param>
public sealed record WorkspaceScanResult(IReadOnlyList<string> SolutionPaths, IReadOnlyList<string> ProjectPaths)
{
    public static WorkspaceScanResult Empty { get; } = new([], []);

    public bool IsEmpty => SolutionPaths.Count == 0 && ProjectPaths.Count == 0;
}
