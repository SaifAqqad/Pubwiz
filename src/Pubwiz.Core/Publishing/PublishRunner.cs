using Pubwiz.Core.Profiles;
using System.Diagnostics;

namespace Pubwiz.Core.Publishing;

/// <summary>
/// Creates publish jobs and runs them, one or several at a time.
/// </summary>
public sealed class PublishRunner(IEnumerable<IPublishTarget> targets)
{
    private readonly IReadOnlyList<IPublishTarget> _targets = targets.ToList();

    /// <summary>The target that publishes <paramref name="profile"/>, or <c>null</c> when none does yet.</summary>
    public IPublishTarget? FindTarget(PublishProfile profile) => _targets.FirstOrDefault(t => t.CanPublish(profile));

    /// <summary>
    /// Creates a job for a request. A profile no target supports (or can't run here) gives a job that fails straight away,
    /// so a batch still runs the other projects.
    /// </summary>
    public PublishJob CreateJob(PublishRequest request)
    {
        if (FindTarget(request.Profile) is not { } target)
        {
            return new PublishJob(request, targetType: null, steps: [], setupError: $"Publishing with '{request.Profile.Method}' profiles isn't supported yet.");
        }

        if (target.GetSetupError(request.Profile) is { } setupError)
        {
            return new PublishJob(request, target.Type, steps: [], setupError);
        }

        return new PublishJob(request, target.Type, target.CreateSteps(request), setupError: null);
    }

    /// <summary>
    /// Runs the jobs. Failures are recorded on each job instead of being thrown.
    /// </summary>
    /// <param name="maxParallelism">How many jobs run at the same time (1 = one after another).</param>
    public async Task RunAsync(IReadOnlyList<PublishJob> jobs, int maxParallelism, CancellationToken cancellationToken)
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, maxParallelism) };

        // The token isn't passed to ForEachAsync: cancelling must still let every job mark itself as cancelled.
        await Parallel.ForEachAsync(jobs, options, async (job, _) => await RunJobAsync(job, cancellationToken));
    }

    private static async Task RunJobAsync(PublishJob job, CancellationToken cancellationToken)
    {
        job.StartedAt = DateTimeOffset.Now;

        if (job.Error is not null)
        {
            job.Log.Info(job.Error);
            Finish(job, JobStatus.Failed);
            return;
        }

        job.Status = JobStatus.Running;
        job.Log.Info($"Publishing {job.Request.Project.Name} with profile '{job.Request.Profile.Name}' ({job.Request.EffectiveConfiguration})");

        var context = new PublishContext(job);
        foreach (var step in job.Steps)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                SkipRemaining(job);
                Finish(job, JobStatus.Cancelled);
                return;
            }

            if (!await RunStepAsync(job, step, context, cancellationToken))
            {
                SkipRemaining(job);
                Finish(job, cancellationToken.IsCancellationRequested ? JobStatus.Cancelled : JobStatus.Failed);
                return;
            }
        }

        Finish(job, JobStatus.Succeeded);
    }

    /// <returns><c>true</c> when the step succeeded.</returns>
    private static async Task<bool> RunStepAsync(PublishJob job, PublishStep step, PublishContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        step.Status = StepStatus.Running;
        job.Log.CurrentStep = step.Name;
        job.Log.Info($"▶ {step.Name}");

        try
        {
            await step.ExecuteAsync(context, cancellationToken);
            step.Status = StepStatus.Succeeded;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            step.Error = "Cancelled";
            step.Status = StepStatus.Failed;
            job.Error = "Cancelled";
            return false;
        }
        catch (Exception ex)
        {
            // Expected failures carry a user-facing message; anything else is a bug, so keep its type in the log.
            var message = ex is PublishStepException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
            job.Log.Write(LogSource.Error, message);
            step.Error = message;
            step.Status = StepStatus.Failed;
            job.Error = message;
            return false;
        }
        finally
        {
            step.Duration = stopwatch.Elapsed;
            step.Detail = null;
            job.Log.CurrentStep = null;
        }
    }

    private static void SkipRemaining(PublishJob job)
    {
        foreach (var step in job.Steps.Where(s => s.Status == StepStatus.Pending))
        {
            step.Status = StepStatus.Skipped;
        }
    }

    private static void Finish(PublishJob job, JobStatus status)
    {
        job.Status = status;
        job.FinishedAt = DateTimeOffset.Now;
        job.Log.Info($"Finished: {status}");
    }
}
