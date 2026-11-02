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
/// When the batch doesn't fit the terminal, finished and waiting jobs are shortened so the running ones stay in view
/// (see <see cref="DashboardLayout"/>).
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
        var frame = 0;

        // Without a terminal (output piped, CI) there's nothing to update in place: only the record below is written.
        if (console.Profile.Capabilities.Interactive)
        {
            await console.Live(RenderLive(jobs, frame, showLog, options.LogLines))
                .AutoClear(true)
                .Overflow(VerticalOverflow.Ellipsis)
                .StartAsync(async context =>
                {
                    while (!run.IsCompleted)
                    {
                        if (IsKeyPressed(ConsoleKey.L))
                        {
                            showLog = !showLog;
                        }

                        context.UpdateTarget(RenderLive(jobs, ++frame, showLog, options.LogLines));
                        await Task.WhenAny(run, Task.Delay(RefreshInterval));
                    }
                });
        }
        else
        {
            // Waits without throwing; the `await run` at the end reports a failure after the record is written.
            await Task.WhenAny(run);
        }

        // The live view clears itself; the final state is written as normal output (no log tails or key hints), so it
        // stays on screen as a record of the run. A batch gets one line per project: the results table follows anyway.
        var record = new DashboardView(frame, ShowLog: false, options.LogLines, console.Profile.Width, Live: false);
        var detail = jobs.Count > 1 ? DashboardDetail.RunningExpanded : DashboardDetail.Full;
        console.Write(ToRows(DashboardLayout.Build(jobs, record, detail)));
        console.WriteLine();

        await run;
    }

    private IRenderable RenderLive(IReadOnlyList<PublishJob> jobs, int frame, bool showLog, int logLines)
    {
        var view = new DashboardView(frame, showLog, logLines, console.Profile.Width, Live: true);

        // One line is left for the cursor, so the live area never scrolls the terminal.
        return ToRows(DashboardLayout.Fit(jobs, view, maxLines: Math.Max(2, console.Profile.Height - 1)));
    }

    private static Rows ToRows(IReadOnlyList<string> lines) =>
        new(lines.Select(line => line.Length == 0 ? Text.Empty : (IRenderable)new Markup(line)));

    private bool IsKeyPressed(ConsoleKey key)
    {
        var pressed = false;
        while (console.Input.IsKeyAvailable())
        {
            pressed |= console.Input.ReadKey(intercept: true)?.Key == key;
        }

        return pressed;
    }
}
