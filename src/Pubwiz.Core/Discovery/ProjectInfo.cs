namespace Pubwiz.Core.Discovery;

/// <summary>
/// A project found in the workspace, with the facts the wizard needs.
/// </summary>
public sealed record ProjectInfo
{
    /// <summary>The project file name without extension, e.g. <c>MyApi</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Full path of the project file.</summary>
    public required string FilePath { get; init; }

    public required ProjectKind Kind { get; init; }

    /// <summary>Target frameworks, e.g. <c>net10.0</c> or <c>net10.0-android</c>.</summary>
    public IReadOnlyList<string> TargetFrameworks { get; init; } = [];

    /// <summary>The solution folder the project is in, e.g. <c>/src/</c>, when loaded from a solution.</summary>
    public string? SolutionFolder { get; init; }

    /// <summary>Why the project could not be evaluated, or <c>null</c> when it was evaluated fine.</summary>
    public string? EvaluationError { get; init; }

    /// <summary>The folder containing the project file.</summary>
    public string Directory => Path.GetDirectoryName(FilePath)!;

    public bool IsPublishable => EvaluationError is null && Kind.IsPublishable();
}
