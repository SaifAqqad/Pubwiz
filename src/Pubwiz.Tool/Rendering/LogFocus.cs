using Pubwiz.Core.Publishing;

namespace Pubwiz.Tool.Rendering;

/// <summary>
/// Which job's log the dashboard shows: the first running job, until the user picks another with ←/→. When no job is
/// running, it stays on the last one shown, so a failure can still be read.
/// </summary>
internal sealed class LogFocus
{
    private PublishJob? _picked;
    private PublishJob? _followed;

    /// <summary>The job whose log to show now, or <c>null</c> while none has started.</summary>
    public PublishJob? Current(IReadOnlyList<PublishJob> jobs)
    {
        if (_picked is not null)
        {
            return _picked;
        }

        _followed = jobs.FirstOrDefault(job => job.Status == JobStatus.Running) ?? _followed;
        return _followed;
    }

    /// <summary>Whether there's more than one job to choose from.</summary>
    public static bool CanSwitch(IReadOnlyList<PublishJob> jobs) => jobs.Count(HasLog) > 1;

    /// <summary>Picks the next (1) or previous (-1) job that has started, wrapping around.</summary>
    public void Move(IReadOnlyList<PublishJob> jobs, int direction)
    {
        var started = jobs.Where(HasLog).ToList();
        if (started.Count == 0)
        {
            return;
        }

        var index = Current(jobs) is { } current ? started.IndexOf(current) : -1;
        _picked = index < 0 ? started[0] : started[(index + direction + started.Count) % started.Count];
    }

    /// <summary>Jobs that haven't started (waiting, or cancelled before their turn) have no log.</summary>
    private static bool HasLog(PublishJob job) => job.StartedAt is not null;
}
