using Pubwiz.Core.Publishing;
using Spectre.Console.Rendering;
using System.Diagnostics;

namespace Pubwiz.Tool.Rendering;

/// <summary>
/// Options for the live publish view.
/// </summary>
/// <param name="ShowLog">Start with the log panel visible.</param>
/// <param name="LogLines">How many lines the log panel shows.</param>
public sealed record DashboardOptions(bool ShowLog, int LogLines);

/// <summary>
/// Shows the jobs' steps live while they run, with an optional log panel for one job at a time: L shows or hides it,
/// ←/→ switch to another job's log. When the batch doesn't fit the terminal, finished and waiting jobs are shortened
/// so the running ones stay in view (see <see cref="DashboardLayout"/>).
/// </summary>
/// <remarks>
/// The jobs are updated by the runner on other threads; the dashboard just redraws them every
/// <see cref="RefreshInterval"/>, so no events or locking are needed here. Only the lines that changed are rewritten
/// (see <see cref="LiveRegion"/>).
/// </remarks>
public sealed class PublishDashboard(IAnsiConsole console)
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>How often progress is checked when there's no live view.</summary>
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Runs <paramref name="run"/> while showing <paramref name="jobs"/>. Returns when <paramref name="run"/> completes.
    /// </summary>
    public async Task ShowAsync(IReadOnlyList<PublishJob> jobs, Task run, DashboardOptions options)
    {
        var frame = 0;

        // The live view redraws with escape codes, so it needs a terminal that understands them. Otherwise (output
        // piped, CI, very old consoles) progress is printed as plain lines.
        if (console.Profile.Capabilities is { Interactive: true, Ansi: true })
        {
            var showLog = options.ShowLog;
            var focus = new LogFocus();
            using var region = new LiveRegion(console);
            while (!run.IsCompleted)
            {
                showLog = HandleKeys(jobs, showLog, focus);
                region.Update(RenderLive(jobs, ++frame, showLog, focus, options.LogLines));
                await Task.WhenAny(run, Task.Delay(RefreshInterval));
            }
        }
        else
        {
            await PrintProgressAsync(jobs, run);
        }

        // The live view clears itself; the final state is written as normal output (no log or key hints), so it
        // stays on screen as a record of the run. A batch gets one line per project: the results table follows anyway.
        var record = new DashboardView(frame, ShowLog: false, options.LogLines, console.Profile.Width, Live: false);
        var detail = jobs.Count > 1 ? DashboardDetail.RunningExpanded : DashboardDetail.Full;
        console.Write(ToRows(DashboardLayout.Build(jobs, record, detail)));
        console.WriteLine();

        await run;
    }

    /// <summary>
    /// Without the live view: a line whenever the progress has changed, checked every <see cref="ProgressInterval"/>,
    /// so long builds don't look stuck. Only appends lines, so it works on any console and in CI logs.
    /// </summary>
    /// <remarks>Waits without throwing; the <c>await run</c> in <see cref="ShowAsync"/> reports a failure after the record is written.</remarks>
    private async Task PrintProgressAsync(IReadOnlyList<PublishJob> jobs, Task run)
    {
        var elapsed = Stopwatch.StartNew();
        string? printed = null;
        while (!run.IsCompleted)
        {
            await Task.WhenAny(run, Task.Delay(ProgressInterval));

            var progress = DashboardLayout.ProgressLine(jobs);
            if (!run.IsCompleted && progress != printed)
            {
                var time = $"{(int)elapsed.Elapsed.TotalMinutes}:{elapsed.Elapsed.Seconds:00}";
                console.MarkupLine($"[dim]{time}[/] {progress.EscapeMarkup()}");
                printed = progress;
            }
        }
    }

    private IReadOnlyList<string> RenderLive(IReadOnlyList<PublishJob> jobs, int frame, bool showLog, LogFocus focus, int logLines)
    {
        var view = new DashboardView(frame, showLog, logLines, console.Profile.Width, Live: true,
            LogJob: showLog ? focus.Current(jobs) : null, CanSwitchLog: LogFocus.CanSwitch(jobs));

        // One line is left for the cursor, so the live area never scrolls the terminal.
        return DashboardLayout.Fit(jobs, view, maxLines: Math.Max(2, console.Profile.Height - 1));
    }

    private static Rows ToRows(IReadOnlyList<string> lines) =>
        new(lines.Select(line => line.Length == 0 ? Text.Empty : (IRenderable)new Markup(line)));

    /// <summary>Reacts to the keys pressed since the last frame.</summary>
    /// <returns>Whether the log panel is shown now.</returns>
    private bool HandleKeys(IReadOnlyList<PublishJob> jobs, bool showLog, LogFocus focus)
    {
        while (console.Input.IsKeyAvailable())
        {
            switch (console.Input.ReadKey(intercept: true)?.Key)
            {
                case ConsoleKey.L:
                    showLog = !showLog;
                    break;
                case ConsoleKey.RightArrow or ConsoleKey.Tab when showLog:
                    focus.Move(jobs, 1);
                    break;
                case ConsoleKey.LeftArrow when showLog:
                    focus.Move(jobs, -1);
                    break;
            }
        }

        return showLog;
    }
}
