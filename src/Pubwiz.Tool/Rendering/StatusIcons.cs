using Pubwiz.Core.Publishing;

namespace Pubwiz.Tool.Rendering;

/// <summary>
/// Icons and formatting shared by the live dashboard and the results table.
/// </summary>
public static class StatusIcons
{
    private static readonly Spinner Spinner = Spinner.Known.Dots;

    /// <summary>The spinner frame for animation step <paramref name="frame"/>.</summary>
    public static string SpinnerFrame(int frame) => $"[{Widgets.PrimaryColorMarkup}]{Spinner.Frames[frame % Spinner.Frames.Count]}[/]";

    public static string ForStep(StepStatus status, int frame) => status switch
    {
        StepStatus.Running => SpinnerFrame(frame),
        StepStatus.Succeeded => "[green]✓[/]",
        StepStatus.Failed => "[red]✕[/]",
        StepStatus.Skipped => "[grey]–[/]",
        _ => "[grey]○[/]"
    };

    public static string ForJob(JobStatus status, int frame) => status switch
    {
        JobStatus.Running => SpinnerFrame(frame),
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

    /// <summary>e.g. <c>850ms</c>, <c>12.3s</c>, <c>2m 05s</c>.</summary>
    public static string FormatDuration(TimeSpan? duration) => duration switch
    {
        null => string.Empty,
        { TotalSeconds: < 1 } d => $"{d.TotalMilliseconds:0}ms",
        { TotalMinutes: < 1 } d => $"{d.TotalSeconds:0.0}s",
        var d => $"{(int)d.Value.TotalMinutes}m {d.Value.Seconds:00}s"
    };
}
