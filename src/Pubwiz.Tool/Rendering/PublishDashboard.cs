using Pubwiz.Core.Publishing;
using Spectre.Console.Rendering;

namespace Pubwiz.Tool.Rendering;

/// <summary>
/// Options for the live publish view.
/// </summary>
/// <param name="ShowLog">Start with the log tail visible.</param>
/// <param name="LogLines">How many log lines the tail shows per running job.</param>
public sealed record DashboardOptions(bool ShowLog, int LogLines);

/// <summary>
/// Shows the jobs' steps live while they run, with an optional tail of each running job's log (toggled with L).
/// </summary>
/// <remarks>
/// The jobs are updated by the runner on other threads; the dashboard just redraws them every
/// <see cref="RefreshInterval"/>, so no events or locking are needed here.
/// </remarks>
public sealed class PublishDashboard(IAnsiConsole console)
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Runs <paramref name="run"/> while showing <paramref name="jobs"/>. Returns when <paramref name="run"/> completes.
    /// </summary>
    public async Task ShowAsync(IReadOnlyList<PublishJob> jobs, Task run, DashboardOptions options)
    {
        var showLog = options.ShowLog;
        var interactive = console.Profile.Capabilities.Interactive;
        var frame = 0;

        await console.Live(Render(jobs, frame, showLog, options.LogLines, interactive, console.Profile.Width))
            .AutoClear(true)
            .Overflow(VerticalOverflow.Ellipsis)
            .StartAsync(async context =>
            {
                while (!run.IsCompleted)
                {
                    if (interactive && IsKeyPressed(ConsoleKey.L))
                    {
                        showLog = !showLog;
                    }

                    context.UpdateTarget(Render(jobs, ++frame, showLog, options.LogLines, interactive, console.Profile.Width));
                    await Task.WhenAny(run, Task.Delay(RefreshInterval));
                }
            });

        // The live view clears itself; the final state is written as normal output (no log tails or key hints)
        // so it stays on screen as a record of the run.
        console.Write(Render(jobs, frame, showLog: false, options.LogLines, showHints: false, console.Profile.Width));

        await run;
    }

    private bool IsKeyPressed(ConsoleKey key)
    {
        var pressed = false;
        while (console.Input.IsKeyAvailable())
        {
            pressed |= console.Input.ReadKey(intercept: true)?.Key == key;
        }

        return pressed;
    }

    /// <param name="width">Terminal width: single-line output is cut to it so the view never jumps around.</param>
    private static IRenderable Render(IReadOnlyList<PublishJob> jobs, int frame, bool showLog, int logLines, bool showHints, int width)
    {
        var rows = new List<IRenderable>();
        foreach (var job in jobs)
        {
            rows.Add(RenderJobHeader(job, frame));
            rows.AddRange(job.Steps.Select(step => RenderStep(step, frame, width)));

            if (job.Steps.Count == 0 && job.Error is not null)
            {
                rows.Add(new Markup($"    [red]{job.Error.EscapeMarkup()}[/]"));
            }

            if (showLog && job.Status == JobStatus.Running)
            {
                rows.AddRange(job.Log.Tail(logLines).Select(entry => RenderLogLine(entry.Text, width)));
            }

            rows.Add(Text.Empty);
        }

        if (showHints)
        {
            rows.Add(new Markup($"[dim]L {(showLog ? "hide" : "show")} log · Ctrl+C cancel[/]"));
        }

        return new Rows(rows);
    }

    private static Markup RenderJobHeader(PublishJob job, int frame)
    {
        var target = job.TargetType?.ToString() ?? "unsupported";
        var elapsed = job.StartedAt is { } start ? StatusIcons.FormatDuration((job.FinishedAt ?? DateTimeOffset.Now) - start) : string.Empty;

        return new Markup(
            $"{StatusIcons.ForJob(job.Status, frame)} [bold]{job.Request.Project.Name.EscapeMarkup()}[/] " +
            $"[dim]{job.Request.Profile.Name.EscapeMarkup()} · {target} · {job.Request.EffectiveConfiguration}[/]  [dim]{elapsed}[/]");
    }

    private static IRenderable RenderStep(PublishStep step, int frame, int width)
    {
        var line = $"  {StatusIcons.ForStep(step.Status, frame)} {step.Name.EscapeMarkup()}";

        line += step.Status switch
        {
            StepStatus.Running when step.Progress is { } progress => $" [{Widgets.PrimaryColorMarkup}]{progress:P0}[/]",
            StepStatus.Succeeded or StepStatus.Failed => $" [dim]{StatusIcons.FormatDuration(step.Duration)}[/]",
            _ => string.Empty
        };

        if (step.Status == StepStatus.Running && step.Detail is { } detail)
        {
            // Room left after the indent, icon, name, progress and spacing.
            var room = width - step.Name.Length - 12;
            line += $"  [dim]{DisplayText.Truncate(detail, room).EscapeMarkup()}[/]";
        }

        if (step.Status == StepStatus.Failed && step.Error is { } error)
        {
            line += $"{Environment.NewLine}    [red]{error.EscapeMarkup()}[/]";
        }

        return new Markup(line);
    }

    private static Markup RenderLogLine(string text, int width)
    {
        const string prefix = "    │ ";
        return new Markup($"[grey]{prefix}{DisplayText.Truncate(text, width - prefix.Length - 1).EscapeMarkup()}[/]");
    }
}
