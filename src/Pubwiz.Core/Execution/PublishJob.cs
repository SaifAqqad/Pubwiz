using Pubwiz.Core.Logging;
using Pubwiz.Core.Targets;

namespace Pubwiz.Core.Execution;

public enum JobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

/// <summary>
/// One project being published: its steps, live state, log and outcome.
/// Created by <see cref="PublishRunner.CreateJob"/>.
/// </summary>
public sealed class PublishJob
{
    internal PublishJob(PublishRequest request, PublishTargetType? targetType, IReadOnlyList<PublishStep> steps, string? setupError)
    {
        Request = request;
        TargetType = targetType;
        Steps = steps;
        Error = setupError;
        Log.AddSecret(request.Password);
    }

    public PublishRequest Request { get; }

    /// <summary>The target publishing this job, or <c>null</c> when no target supports the profile.</summary>
    public PublishTargetType? TargetType { get; }

    public IReadOnlyList<PublishStep> Steps { get; }

    public PublishLog Log { get; } = new();

    public JobStatus Status { get; internal set; } = JobStatus.Queued;

    public DateTimeOffset? StartedAt { get; internal set; }

    public DateTimeOffset? FinishedAt { get; internal set; }

    public TimeSpan? Duration => StartedAt is { } start && FinishedAt is { } end ? end - start : null;

    /// <summary>Why the job failed, or <c>null</c>.</summary>
    public string? Error { get; internal set; }

    /// <summary>The step that is running now.</summary>
    public PublishStep? CurrentStep => Steps.FirstOrDefault(s => s.Status == StepStatus.Running);

    /// <summary>The step that failed, if any.</summary>
    public PublishStep? FailedStep => Steps.FirstOrDefault(s => s.Status == StepStatus.Failed);

    /// <inheritdoc cref="PublishContext.OutputDirectory"/>
    public string? OutputDirectory { get; internal set; }

    /// <inheritdoc cref="PublishContext.PackagePath"/>
    public string? PackagePath { get; internal set; }

    /// <inheritdoc cref="PublishContext.Destination"/>
    public string? Destination { get; internal set; }

    /// <summary>Where the app ended up, for display: the destination, or the publish folder for local targets.</summary>
    public string? Result => Destination ?? OutputDirectory;

    public bool IsFinished => Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled;
}
