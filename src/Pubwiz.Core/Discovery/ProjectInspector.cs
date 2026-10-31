using Pubwiz.Core.Dotnet;
using System.Collections.Concurrent;

namespace Pubwiz.Core.Discovery;

/// <summary>
/// Loads <see cref="ProjectInfo"/> for project files by evaluating them with MSBuild.
/// </summary>
public sealed class ProjectInspector(DotnetCli dotnet)
{
    private static readonly string[] Properties = [.. ProjectKindDetector.RequiredProperties, "TargetFramework", "TargetFrameworks"];

    /// <summary>
    /// How many <c>dotnet msbuild</c> processes evaluate projects at the same time. Each one evaluates a share of the
    /// projects; a few large shares beat many small ones, because a process loads the SDK files only once.
    /// </summary>
    private static readonly int MaxProcesses = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    /// <summary>
    /// Evaluates the projects, a share of them per <c>dotnet msbuild</c> process. Projects that fail to evaluate are
    /// returned with <see cref="ProjectInfo.EvaluationError"/> set instead of throwing.
    /// </summary>
    /// <returns>The projects, in the same order as <paramref name="projects"/>.</returns>
    public async Task<IReadOnlyList<ProjectInfo>> InspectAsync(IReadOnlyList<SolutionProject> projects, CancellationToken cancellationToken = default)
    {
        var evaluations = await EvaluateInSharesAsync(projects.Select(p => p.FilePath).Distinct().ToList(), cancellationToken);

        var results = new ProjectInfo[projects.Count];
        await Parallel.ForAsync(0, projects.Count, cancellationToken, async (index, ct) =>
        {
            var project = projects[index];
            results[index] = evaluations.TryGetValue(Path.GetFullPath(project.FilePath), out var evaluation)
                ? CreateInfo(project, evaluation)
                // Failed in the shared run: evaluate it on its own, which also reports the error.
                : await InspectAsync(project, ct);
        });

        return results;
    }

    public async Task<ProjectInfo> InspectAsync(SolutionProject project, CancellationToken cancellationToken = default)
    {
        try
        {
            var evaluation = await dotnet.EvaluateAsync(project.FilePath, Properties, ProjectKindDetector.RequiredItems, cancellationToken);
            return CreateInfo(project, evaluation);
        }
        catch (MsBuildEvaluationException ex)
        {
            return CreateInfo(project, evaluation: null) with { EvaluationError = ex.Error };
        }
    }

    /// <summary>
    /// Splits the projects into one share per process and evaluates the shares in parallel.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, MsBuildEvaluation>> EvaluateInSharesAsync(IReadOnlyList<string> projectPaths, CancellationToken cancellationToken)
    {
        var shareSize = Math.Max((int)Math.Ceiling(projectPaths.Count / (double)MaxProcesses), 1);
        var evaluations = new ConcurrentDictionary<string, MsBuildEvaluation>(StringComparer.OrdinalIgnoreCase);

        await Parallel.ForEachAsync(projectPaths.Chunk(shareSize), cancellationToken, async (share, ct) =>
        {
            foreach (var (path, evaluation) in await dotnet.EvaluateManyAsync(share, Properties, ProjectKindDetector.RequiredItems, ct))
            {
                evaluations[path] = evaluation;
            }
        });

        return evaluations;
    }

    private static ProjectInfo CreateInfo(SolutionProject project, MsBuildEvaluation? evaluation) => new()
    {
        Name = Path.GetFileNameWithoutExtension(project.FilePath),
        FilePath = project.FilePath,
        SolutionFolder = project.SolutionFolder,
        Kind = evaluation is null ? ProjectKind.Unknown : ProjectKindDetector.Detect(evaluation),
        TargetFrameworks = evaluation is null ? [] : GetTargetFrameworks(evaluation)
    };

    private static IReadOnlyList<string> GetTargetFrameworks(MsBuildEvaluation evaluation)
    {
        var value = evaluation.GetProperty("TargetFrameworks") ?? evaluation.GetProperty("TargetFramework") ?? string.Empty;
        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
