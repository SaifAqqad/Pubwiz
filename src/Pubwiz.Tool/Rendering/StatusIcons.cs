using Pubwiz.Core.Publishing;

namespace Pubwiz.Tool.Rendering;

/// <summary>
/// Icons and formatting shared by the live dashboard and the results table.
/// </summary>
public static class StatusIcons
{
    /// <summary>The spinner's frames, drawn by the live view in place of <see cref="LiveRegion.Spinner"/>.</summary>
    public static IReadOnlyList<string> SpinnerFrames { get; } = Spinner.Known.Dots.Frames;

    /// <summary>
    /// What's running: a spinner in the live view (where <see cref="LiveRegion"/> animates it by redrawing just that
    /// character), otherwise a still mark.
    /// </summary>
    private static string Running(bool live) => live
        ? $"[{Widgets.PrimaryColorMarkup}]{LiveRegion.Spinner}[/]"
        : $"[{Widgets.PrimaryColorMarkup} bold]»[/]";

    public static string ForStep(StepStatus status, bool live = false) => status switch
    {
        StepStatus.Running => Running(live),
        StepStatus.Succeeded => "[green]✓[/]",
        StepStatus.Failed => "[red]✕[/]",
        StepStatus.Skipped => "[grey]–[/]",
        _ => "[grey]○[/]"
    };

    public static string ForJob(JobStatus status, bool live = false) => status switch
    {
        JobStatus.Running => Running(live),
        JobStatus.Succeeded => "[green]✓[/]",
        JobStatus.Failed => "[red]✕[/]",
        JobStatus.Cancelled => "[yellow]–[/]",
        _ => "[grey]○[/]"
    };

    public static string JobStatusText(JobStatus status) => status switch
    {
        JobStatus.Succeeded => "[green]Succeeded[/]",
        JobStatus.Failed => "[red]Failed[/]",
        JobStatus.Cancelled => "[yellow]Cancelled[/]",
        JobStatus.Running => $"[{Widgets.PrimaryColorMarkup}]Running[/]",
        _ => "[grey]Queued[/]"
    };

    /// <summary>
    /// How long something has been running, in whole seconds (<c>12s</c>, <c>2m 05s</c>), so the live view changes
    /// its line once a second instead of on every redraw.
    /// </summary>
    public static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalMinutes < 1
        ? $"{(int)elapsed.TotalSeconds}s"
        : $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:00}s";

    /// <summary>e.g. <c>850ms</c>, <c>12.3s</c>, <c>2m 05s</c>.</summary>
    public static string FormatDuration(TimeSpan? duration) => duration switch
    {
        null => string.Empty,
        { TotalSeconds: < 1 } d => $"{d.TotalMilliseconds:0}ms",
        { TotalMinutes: < 1 } d => $"{d.TotalSeconds:0.0}s",
        var d => $"{(int)d.Value.TotalMinutes}m {d.Value.Seconds:00}s"
    };
}
