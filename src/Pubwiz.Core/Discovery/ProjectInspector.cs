using Pubwiz.Core.Dotnet;

namespace Pubwiz.Core.Discovery;

/// <summary>
/// Loads <see cref="ProjectInfo"/> for project files by evaluating them with MSBuild.
/// </summary>
public sealed class ProjectInspector(DotnetCli dotnet)
{
    private static readonly string[] Properties = [.. ProjectKindDetector.RequiredProperties, "TargetFramework", "TargetFrameworks"];

    /// <summary>
    /// Evaluates the projects in parallel. Projects that fail to evaluate are returned
    /// with <see cref="ProjectInfo.EvaluationError"/> set instead of throwing.
    /// </summary>
    /// <returns>The projects, in the same order as <paramref name="projects"/>.</returns>
    public async Task<IReadOnlyList<ProjectInfo>> InspectAsync(IReadOnlyList<SolutionProject> projects, CancellationToken cancellationToken = default)
    {
        var results = new ProjectInfo[projects.Count];
        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(Environment.ProcessorCount / 2, 1)
        };

        await Parallel.ForAsync(0, projects.Count, options, async (index, ct) => results[index] = await InspectAsync(projects[index], ct));
        return results;
    }

    public async Task<ProjectInfo> InspectAsync(SolutionProject project, CancellationToken cancellationToken = default)
    {
        var info = new ProjectInfo
        {
            Name = Path.GetFileNameWithoutExtension(project.FilePath),
            FilePath = project.FilePath,
            SolutionFolder = project.SolutionFolder,
            Kind = ProjectKind.Unknown
        };

        try
        {
            var evaluation = await dotnet.EvaluateAsync(project.FilePath, Properties, ProjectKindDetector.RequiredItems, cancellationToken);
            return info with
            {
                Kind = ProjectKindDetector.Detect(evaluation),
                TargetFrameworks = GetTargetFrameworks(evaluation)
            };
        }
        catch (MsBuildEvaluationException ex)
        {
            return info with { EvaluationError = ex.Error };
        }
    }

    private static IReadOnlyList<string> GetTargetFrameworks(MsBuildEvaluation evaluation)
    {
        var value = evaluation.GetProperty("TargetFrameworks") ?? evaluation.GetProperty("TargetFramework") ?? string.Empty;
        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
