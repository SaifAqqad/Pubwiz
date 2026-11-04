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

    /// <summary>Like <see cref="Summarized"/>, but running jobs show only the step they're on.</summary>
    Minimal
}

/// <summary>What the dashboard is drawing right now.</summary>
/// <param name="ShowLog">Show the log panel (live view only).</param>
/// <param name="LogLines">How many lines the log panel shows.</param>
/// <param name="Width">The terminal width.</param>
/// <param name="Live">
/// The live view: every line is cut to <paramref name="Width"/> so the line count is exact, and the key hints are shown.
/// Otherwise (the record written after the run) errors are shown in full.
/// </param>
/// <param name="LogJob">The job whose log the panel shows; <c>null</c> until a job starts.</param>
/// <param name="CanSwitchLog">Whether there are other jobs to switch the log panel to.</param>
internal sealed record DashboardView(bool ShowLog, int LogLines, int Width, bool Live, PublishJob? LogJob = null, bool CanSwitchLog = false);

/// <summary>
/// Turns the jobs into the dashboard's lines (markup, one terminal line each in the live view). A batch of many
/// projects doesn't fit on screen in full, so the live view uses the most detailed <see cref="DashboardDetail"/>
/// that fits the terminal's height; the running jobs always stay in view. The log panel, when shown, has a fixed
/// height below the jobs, so switching detail levels never moves it.
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

    /// <summary>
    /// One line of progress for consoles without the live view, in plain text, e.g.
    /// <c>3/10 done · running: App4 (Build and publish), App5 (Upload (FTP)) · 5 waiting</c>.
    /// </summary>
    internal static string ProgressLine(IReadOnlyList<PublishJob> jobs)
    {
        var parts = new List<string> { $"{jobs.Count(j => j.IsFinished)}/{jobs.Count} done" };
        if (CurrentStep(jobs) is { } step)
        {
            parts.Insert(0, $"step {step.Number}/{step.Count}");
        }

        var failed = jobs.Count(j => j.Status == JobStatus.Failed);
        if (failed > 0)
        {
            parts[0] += $", {failed} failed";
        }

        var running = jobs.Where(j => j.Status == JobStatus.Running)
            .Select(j => j.Steps.FirstOrDefault(s => s.Status == StepStatus.Running) is { } step ? $"{j.Request.Project.Name} ({step.Name})" : j.Request.Project.Name)
            .ToList();
        if (running.Count > 0)
        {
            parts.Add($"running: {string.Join(", ", running)}");
        }

        var waiting = jobs.Count(j => j.Status == JobStatus.Queued);
        if (waiting > 0)
        {
            parts.Add($"{waiting} waiting");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>The lines at one level of detail. An empty string is a blank line.</summary>
    internal static IReadOnlyList<string> Build(IReadOnlyList<PublishJob> jobs, DashboardView view, DashboardDetail detail)
    {
        var lines = new List<string>();
        var summarize = detail >= DashboardDetail.Summarized;

        // A batch published in several steps shows them: a heading per step, or just the current step when summarized.
        var stepCount = jobs.Select(j => j.Request.Order).Distinct().Count();
        if (summarize && CurrentStep(jobs) is { } current)
        {
            AddSection(lines, [$"[{Widgets.PrimaryColorMarkup} bold]Step {current.Number} of {current.Count}[/]"]);
        }

        if (summarize)
        {
            AddSection(lines, SummaryLines(jobs.Where(j => j.IsFinished), view));
        }

        int? shownStep = null;
        foreach (var job in jobs)
        {
            var afterHeading = false;
            if (stepCount > 1 && !summarize && job.Request.Order != shownStep)
            {
                shownStep = job.Request.Order;
                AddSection(lines, [StepHeading(jobs, job.Request.Order, stepCount)]);
                afterHeading = true;
            }

            if (job.Status == JobStatus.Running || detail == DashboardDetail.Full)
            {
                var jobLines = JobLines(job, view, currentStepOnly: detail == DashboardDetail.Minimal);
                if (afterHeading)
                {
                    lines.AddRange(jobLines);
                }
                else
                {
                    AddSection(lines, jobLines);
                }
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
            if (view.ShowLog)
            {
                AddSection(lines, LogPanel(view));
            }

            var switchHint = view.ShowLog && view.CanSwitchLog ? " · ←/→ other project's log" : string.Empty;
            AddSection(lines, [$"[dim]L {(view.ShowLog ? "hide" : "show")} log{switchHint} · Ctrl+C cancel[/]"]);
        }

        return lines;
    }

    /// <summary>The step that's publishing (the first with unfinished jobs), when the batch has several steps.</summary>
    private static (int Number, int Count)? CurrentStep(IReadOnlyList<PublishJob> jobs)
    {
        var steps = jobs.Select(j => j.Request.Order).Distinct().Order().ToList();
        if (steps.Count < 2 || jobs.Where(j => !j.IsFinished).Select(j => (int?)j.Request.Order).Min() is not { } current)
        {
            return null;
        }

        return (steps.IndexOf(current) + 1, steps.Count);
    }

    /// <summary>e.g. "Step 1 of 2 · 3 at the same time", in colour while it's publishing.</summary>
    private static string StepHeading(IReadOnlyList<PublishJob> jobs, int order, int stepCount)
    {
        var members = jobs.Where(j => j.Request.Order == order).ToList();
        var number = jobs.Select(j => j.Request.Order).Distinct().Order().ToList().IndexOf(order) + 1;
        var style = members.Any(j => j.Status == JobStatus.Running) ? $"{Widgets.PrimaryColorMarkup} bold"
            : members.All(j => j.IsFinished) ? "dim" : "bold";
        var together = members.Count > 1 ? $" [dim]· {members.Count} at the same time[/]" : string.Empty;
        return $"[{style}]Step {number} of {stepCount}[/]{together}";
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

        return lines;
    }

    /// <summary>
    /// One job's latest log lines under a title, always <see cref="DashboardView.LogLines"/> + 1 lines tall.
    /// </summary>
    private static List<string> LogPanel(DashboardView view)
    {
        var name = view.LogJob?.Request.Project.Name ?? "Log";
        var title = view.LogJob is null ? $"{name} " : $"{name} log ";
        var rule = new string('─', Math.Max(0, view.Width - title.Length - 4));
        var lines = new List<string> { $"[dim]──[/] [bold]{title.EscapeMarkup()}[/][dim]{rule}[/]" };

        var entries = view.LogJob?.Log.Tail(view.LogLines) ?? [];
        if (entries.Count == 0)
        {
            lines.Add(view.LogJob is null ? "[dim]  waiting for a project to start[/]" : "[dim]  no output yet[/]");
        }

        lines.AddRange(entries.Select(entry => LogLine(entry, view)));

        // Padded to a fixed height (continuing the gutter), so the jobs above don't jump around as the log fills up.
        while (lines.Count < view.LogLines + 1)
        {
            lines.Add($"[grey]{LogGutter}[/]");
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
                return $"{StatusIcons.ForJob(group.Key, view.Live)} {label.EscapeMarkup()}[dim]{Fit(names, view.Width - label.Length - 3, view).EscapeMarkup()}[/]";
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
        var elapsed = job.StartedAt is not { } start ? string.Empty
            : job.FinishedAt is { } end ? StatusIcons.FormatDuration(end - start)
            : StatusIcons.FormatElapsed(DateTimeOffset.Now - start);
        var details = $"{job.Request.Profile.Name} · {target} · {job.Request.EffectiveConfiguration}";

        // The name and time always show; the details are cut to what's left of the line. The job whose log is shown
        // has its name in colour, to match the log panel's title.
        var room = view.Width - job.Request.Project.Name.Length - elapsed.Length - 6;
        var nameStyle = view.ShowLog && view.Live && job == view.LogJob ? $"bold {Widgets.PrimaryColorMarkup}" : "bold";
        return $"{StatusIcons.ForJob(job.Status, view.Live)} [{nameStyle}]{job.Request.Project.Name.EscapeMarkup()}[/] " +
               $"[dim]{Fit(details, room, view).EscapeMarkup()}[/]  [dim]{elapsed}[/]";
    }

    private static string StepLine(PublishStep step, DashboardView view)
    {
        var line = $"  {StatusIcons.ForStep(step.Status, view.Live)} {step.Name.EscapeMarkup()}";

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

    private const string LogGutter = "  │";

    /// <summary>A log line, with errors in red and warnings in yellow so they stand out from the build output.</summary>
    private static string LogLine(LogEntry entry, DashboardView view)
    {
        const string prefix = LogGutter + " ";
        var color = entry.Source == LogSource.Error || entry.Text.Contains(": error ", StringComparison.Ordinal) ? "red"
            : entry.Text.Contains(": warning ", StringComparison.Ordinal) ? "yellow"
            : "grey";

        return $"[grey]{prefix}[/][{color}]{DisplayText.Truncate(entry.Text, view.Width - prefix.Length - 1).EscapeMarkup()}[/]";
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
