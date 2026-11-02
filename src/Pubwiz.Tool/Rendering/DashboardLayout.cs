using Pubwiz.Core.Publishing;
using Pubwiz.Core.Targets;

namespace Pubwiz.Tool.Rendering;

/// <summary>How much of each job the dashboard shows, from most to least.</summary>
internal enum DashboardDetail
{
    /// <summary>Every job with all its steps.</summary>
    Full,

    /// <summary>Running jobs with their steps; the others on one line each.</summary>
    RunningExpanded,

    /// <summary>Running jobs with their steps; finished and waiting jobs summed up in a line per status.</summary>
    Summarized,

    /// <summary>Like <see cref="Summarized"/>, but running jobs show only the step they're on, and no log.</summary>
    Minimal
}

/// <summary>What the dashboard is drawing right now.</summary>
/// <param name="Frame">The animation step, for spinners.</param>
/// <param name="ShowLog">Show the tail of each running job's log.</param>
/// <param name="LogLines">How many log lines per running job.</param>
/// <param name="Width">The terminal width.</param>
/// <param name="Live">
/// The live view: every line is cut to <paramref name="Width"/> so the line count is exact, and the key hints are shown.
/// Otherwise (the record written after the run) errors are shown in full.
/// </param>
internal sealed record DashboardView(int Frame, bool ShowLog, int LogLines, int Width, bool Live);

/// <summary>
/// Turns the jobs into the dashboard's lines (markup, one terminal line each in the live view). A batch of many
/// projects doesn't fit on screen in full, so the live view uses the most detailed <see cref="DashboardDetail"/>
/// that fits the terminal's height; the running jobs always stay in view.
/// </summary>
internal static class DashboardLayout
{
    /// <summary>The lines at the most detailed level that fits in <paramref name="maxLines"/>.</summary>
    /// <param name="jobs">The jobs, in batch order.</param>
    /// <param name="view">What to draw.</param>
    /// <param name="maxLines">The terminal height to fit in; <c>null</c> for no limit (full detail).</param>
    public static IReadOnlyList<string> Fit(IReadOnlyList<PublishJob> jobs, DashboardView view, int? maxLines)
    {
        IReadOnlyList<string> lines = [];
        foreach (var detail in Enum.GetValues<DashboardDetail>())
        {
            lines = Build(jobs, view, detail);
            if (maxLines is null || lines.Count <= maxLines)
            {
                return lines;
            }
        }

        // Even the least detail is too tall (many jobs at once in a small window): cut it here, so the live display
        // never has to crop (Spectre.Console can throw while cropping).
        var kept = Math.Max(0, maxLines!.Value - 1);
        return [.. lines.Take(kept), $"[dim]… {lines.Count - kept} more lines (make the window taller to see them)[/]"];
    }

    /// <summary>The lines at one level of detail. An empty string is a blank line.</summary>
    internal static IReadOnlyList<string> Build(IReadOnlyList<PublishJob> jobs, DashboardView view, DashboardDetail detail)
    {
        var lines = new List<string>();
        var summarize = detail >= DashboardDetail.Summarized;

        if (summarize)
        {
            AddSection(lines, SummaryLines(jobs.Where(j => j.IsFinished), view));
        }

        foreach (var job in jobs)
        {
            if (job.Status == JobStatus.Running || detail == DashboardDetail.Full)
            {
                AddSection(lines, JobLines(job, view, currentStepOnly: detail == DashboardDetail.Minimal));
            }
            else if (detail == DashboardDetail.RunningExpanded)
            {
                lines.Add(OneLine(job, view));
            }
        }

        if (summarize)
        {
            AddSection(lines, SummaryLines(jobs.Where(j => j.Status == JobStatus.Queued), view));
        }

        if (view.Live)
        {
            AddSection(lines, [$"[dim]L {(view.ShowLog ? "hide" : "show")} log · Ctrl+C cancel[/]"]);
        }

        return lines;
    }

    /// <summary>Adds a group of lines with a blank line between it and what came before.</summary>
    private static void AddSection(List<string> lines, IReadOnlyList<string> section)
    {
        if (section.Count == 0)
        {
            return;
        }

        if (lines.Count > 0 && lines[^1].Length > 0)
        {
            lines.Add(string.Empty);
        }

        lines.AddRange(section);
    }

    /// <summary>A job's header and steps, plus its error and log tail when there are any.</summary>
    private static List<string> JobLines(PublishJob job, DashboardView view, bool currentStepOnly)
    {
        var lines = new List<string> { Header(job, view) };
        var steps = currentStepOnly ? job.Steps.Where(s => s.Status == StepStatus.Running) : job.Steps;

        foreach (var step in steps)
        {
            lines.Add(StepLine(step, view));
            if (step.Status == StepStatus.Failed && step.Error is { } stepError)
            {
                lines.Add(ErrorLine(stepError, view));
            }
        }

        if (job.Steps.Count == 0 && job.Error is not null)
        {
            lines.Add(ErrorLine(job.Error, view));
        }

        if (view.ShowLog && !currentStepOnly && job.Status == JobStatus.Running)
        {
            lines.AddRange(job.Log.Tail(view.LogLines).Select(entry => LogLine(entry.Text, view)));
        }

        return lines;
    }

    /// <summary>A finished or waiting job on one line: its header, then why it failed.</summary>
    private static string OneLine(PublishJob job, DashboardView view)
    {
        var header = Header(job, view);
        if (job.Status != JobStatus.Failed || job.Error is null)
        {
            return header;
        }

        var room = view.Width - Markup.Remove(header).Length - 3;
        return $"{header}  [red]{Fit(job.Error, room, view).EscapeMarkup()}[/]";
    }

    /// <summary>One line per status, e.g. "✓ 3 published: Api, Web, Worker".</summary>
    private static List<string> SummaryLines(IEnumerable<PublishJob> jobs, DashboardView view)
    {
        return jobs.GroupBy(j => j.Status)
            .OrderBy(g => g.Key)
            .Select(group =>
            {
                var label = $"{group.Count()} {Describe(group.Key)}: ";
                var names = string.Join(", ", group.Select(j => j.Request.Project.Name));
                return $"{StatusIcons.ForJob(group.Key, view.Frame)} {label.EscapeMarkup()}[dim]{Fit(names, view.Width - label.Length - 3, view).EscapeMarkup()}[/]";
            })
            .ToList();

        static string Describe(JobStatus status) => status switch
        {
            JobStatus.Succeeded => "published",
            JobStatus.Failed => "failed",
            JobStatus.Cancelled => "cancelled",
            _ => "waiting"
        };
    }

    private static string Header(PublishJob job, DashboardView view)
    {
        var target = job.TargetType is { } type ? PublishTargetCatalog.DisplayName(type) : "unsupported";
        var elapsed = job.StartedAt is { } start ? StatusIcons.FormatDuration((job.FinishedAt ?? DateTimeOffset.Now) - start) : string.Empty;
        var details = $"{job.Request.Profile.Name} · {target} · {job.Request.EffectiveConfiguration}";

        // The name and time always show; the details are cut to what's left of the line.
        var room = view.Width - job.Request.Project.Name.Length - elapsed.Length - 6;
        return $"{StatusIcons.ForJob(job.Status, view.Frame)} [bold]{job.Request.Project.Name.EscapeMarkup()}[/] " +
               $"[dim]{Fit(details, room, view).EscapeMarkup()}[/]  [dim]{elapsed}[/]";
    }

    private static string StepLine(PublishStep step, DashboardView view)
    {
        var line = $"  {StatusIcons.ForStep(step.Status, view.Frame)} {step.Name.EscapeMarkup()}";

        line += step.Status switch
        {
            StepStatus.Running when step.Progress is { } progress => $" [{Widgets.PrimaryColorMarkup}]{progress:P0}[/]",
            StepStatus.Succeeded or StepStatus.Failed => $" [dim]{StatusIcons.FormatDuration(step.Duration)}[/]",
            _ => string.Empty
        };

        if (step.Status == StepStatus.Running && step.Detail is { } detail)
        {
            // Room left after the indent, icon, name, progress and spacing.
            var room = view.Width - step.Name.Length - 12;
            line += $"  [dim]{DisplayText.Truncate(detail, room).EscapeMarkup()}[/]";
        }

        return line;
    }

    private static string ErrorLine(string error, DashboardView view) => $"    [red]{Fit(error, view.Width - 5, view).EscapeMarkup()}[/]";

    private static string LogLine(string text, DashboardView view)
    {
        const string prefix = "    │ ";
        return $"[grey]{prefix}{DisplayText.Truncate(text, view.Width - prefix.Length - 1).EscapeMarkup()}[/]";
    }

    /// <summary>Cuts text to one line in the live view; the record after the run keeps it whole.</summary>
    private static string Fit(string text, int room, DashboardView view)
    {
        if (!view.Live)
        {
            return text;
        }

        // A multi-line error would take several lines, so only its first line is shown live.
        var firstLine = text.Split('\n', 2)[0].TrimEnd('\r');
        return DisplayText.Truncate(firstLine, room);
    }
}
