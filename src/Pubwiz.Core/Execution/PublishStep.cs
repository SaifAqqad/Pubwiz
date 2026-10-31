namespace Pubwiz.Core.Execution;

public enum StepStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,

    /// <summary>Not run because an earlier step failed or the job was cancelled.</summary>
    Skipped
}

/// <summary>
/// One step of a publish job (build, package, upload, ...). Targets build their job out of steps
/// so the UI can show a checklist before anything runs.
/// </summary>
/// <remarks>
/// The step's state (<see cref="Status"/>, <see cref="Detail"/>, ...) is updated by <see cref="PublishRunner"/>
/// and by the step itself through <see cref="PublishContext"/>, and read by the UI while the job runs.
/// </remarks>
public abstract class PublishStep(string name)
{
    /// <summary>What the step does, e.g. "Build and publish".</summary>
    public string Name { get; } = name;

    public StepStatus Status { get; internal set; } = StepStatus.Pending;

    /// <summary>A short live status, e.g. the file being uploaded.</summary>
    public string? Detail { get; internal set; }

    /// <summary>Progress from 0 to 1 when the step can measure it, otherwise <c>null</c>.</summary>
    public double? Progress { get; internal set; }

    public TimeSpan? Duration { get; internal set; }

    /// <summary>Why the step failed.</summary>
    public string? Error { get; internal set; }

    /// <summary>
    /// Does the work. Throw <see cref="PublishStepException"/> with a clear message to fail the step.
    /// </summary>
    public abstract Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken);
}

/// <summary>
/// An expected failure of a step (a build error, a rejected upload, ...), shown to the user as is.
/// </summary>
public sealed class PublishStepException(string message, Exception? innerException = null) : Exception(message, innerException);
