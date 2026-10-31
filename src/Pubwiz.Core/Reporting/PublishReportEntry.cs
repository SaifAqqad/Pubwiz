using Pubwiz.Core.Publishing;

namespace Pubwiz.Core.Reporting;

/// <summary>
/// One row of the publish report: everything worth knowing about one job, flattened for JSON and CSV.
/// </summary>
public sealed record PublishReportEntry(
    string Project,
    string ProjectPath,
    string Profile,
    string? Target,
    string Configuration,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    double? DurationSeconds,
    string? FailedStep,
    string? Error,
    string? OutputDirectory,
    string? Destination,
    int LogLines)
{
    public static PublishReportEntry FromJob(PublishJob job) => new(
        Project: job.Request.Project.Name,
        ProjectPath: job.Request.Project.FilePath,
        Profile: job.Request.Profile.Name,
        Target: job.TargetType?.ToString(),
        Configuration: job.Request.EffectiveConfiguration,
        Status: job.Status.ToString(),
        StartedAt: job.StartedAt,
        FinishedAt: job.FinishedAt,
        DurationSeconds: job.Duration is { } duration ? Math.Round(duration.TotalSeconds, 2) : null,
        FailedStep: job.FailedStep?.Name,
        Error: job.Error,
        OutputDirectory: job.OutputDirectory,
        Destination: job.Destination,
        LogLines: job.Log.Count);
}
