using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Targets;
using Pubwiz.Tool.Rendering;
using Spectre.Console;

namespace Pubwiz.Tool.Tests.Rendering;

/// <summary>
/// A real batch of 10 projects published 2 at a time, frozen with 3 published, 1 failed, 2 running and 4 waiting.
/// </summary>
public sealed class DashboardLayoutTests : IAsyncLifetime
{
    private const int Width = 80;

    private readonly Dictionary<string, TaskCompletionSource> _gates = new();
    private PublishRunner _runner = null!;
    private List<PublishJob> _jobs = null!;
    private Task _run = null!;

    public async ValueTask InitializeAsync()
    {
        _runner = new PublishRunner([new GatedTarget(_gates)]);
        _jobs = Enumerable.Range(1, 10).Select(i => _runner.CreateJob(Request($"App{i}"))).ToList();
        foreach (var job in _jobs)
        {
            _gates[job.Request.Project.Name] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _run = _runner.RunAsync(_jobs, maxParallelism: 2, CancellationToken.None);

        _gates["App1"].SetResult();
        _gates["App2"].SetResult();
        _gates["App3"].SetResult();
        _gates["App4"].SetException(new PublishStepException("error CS1002: ; expected"));
        await WaitUntilAsync(() => _jobs.Take(4).All(j => j.IsFinished) && _jobs.Count(j => j.Status == JobStatus.Running) == 2);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var gate in _gates.Values)
        {
            gate.TrySetResult();
        }

        await _run;
    }

    [Fact]
    public void ShowsEveryStepWhenItFits()
    {
        var lines = DashboardLayout.Fit(_jobs, LiveView(), maxLines: 100).Select(Plain).ToList();

        // Every job with its step, waiting ones included (the running jobs' times change, so the lines aren't compared).
        Assert.All(_jobs, job => Assert.Contains(lines, line => line.Contains($" {job.Request.Project.Name} ")));
        Assert.Equal(_jobs.Count, lines.Count(line => line.Contains("Build and publish")));
    }

    [Fact]
    public void SumsUpFinishedAndWaitingJobsWhenTheBatchIsTooTall()
    {
        var lines = DashboardLayout.Fit(_jobs, LiveView(), maxLines: 14).Select(Plain).ToList();

        Assert.True(lines.Count <= 14);
        Assert.Contains("✓ 3 published: App1, App2, App3", lines);
        Assert.Contains("✕ 1 failed: App4", lines);
        Assert.Contains("○ 4 waiting: App7, App8, App9, App10", lines);

        // The running jobs stay in view with their step.
        Assert.Contains(lines, line => line.Contains(" App5 "));
        Assert.Contains(lines, line => line.Contains(" App6 "));
        Assert.Equal(2, lines.Count(line => line.Contains("Build and publish")));
    }

    [Fact]
    public void PutsFinishedAndWaitingJobsOnOneLineWhenThatFits()
    {
        var lines = DashboardLayout.Build(_jobs, LiveView(), DashboardDetail.RunningExpanded).Select(Plain).ToList();

        Assert.Contains(lines, line => line.StartsWith("✕ App4 ") && line.Contains("error CS1002"));
        Assert.Equal(2, lines.Count(line => line.Contains("Build and publish")));
    }

    [Fact]
    public void CutsTheLinesWhenEvenTheLeastDetailIsTooTall()
    {
        var lines = DashboardLayout.Fit(_jobs, LiveView(), maxLines: 4);

        Assert.Equal(4, lines.Count);
        Assert.StartsWith("… ", Plain(lines[^1]));
    }

    [Fact]
    public void KeepsEveryLiveLineWithinTheWidth()
    {
        foreach (var detail in Enum.GetValues<DashboardDetail>())
        {
            Assert.All(DashboardLayout.Build(_jobs, LiveView(showLog: true), detail), line => Assert.True(Plain(line).Length <= Width, line));
        }
    }

    [Fact]
    public void ShowsOneJobsLogBelowTheJobs()
    {
        _jobs[4].Log.Info("Restoring packages");
        _jobs[4].Log.Write(LogSource.Output, "Program.cs(3,1): error CS1002: ; expected");
        var view = LiveView(showLog: true) with { LogJob = _jobs[4], CanSwitchLog = true };

        var lines = DashboardLayout.Fit(_jobs, view, maxLines: 100);
        var plain = lines.Select(Plain).ToList();
        var title = plain.FindIndex(line => line.StartsWith("── App5 log "));

        Assert.True(title > plain.FindIndex(line => line.Contains(" App10 ")), "The log comes after the jobs.");

        // The latest lines, with the error in red.
        Assert.Equal("  │ Restoring packages", plain[title + 2]);
        Assert.Contains("[red]", lines[title + 3]);
        Assert.Contains("←/→", plain[^1]);

        // The job whose log is shown is highlighted in the list.
        Assert.Contains($"[bold {Widgets.PrimaryColorMarkup}]App5[/]", lines.Single(line => Plain(line).Contains(" App5 ") && !Plain(line).StartsWith("──")));
    }

    [Fact]
    public void KeepsTheLogPanelTheSameHeight()
    {
        var empty = DashboardLayout.Build(_jobs, LiveView(showLog: true) with { LogJob = _jobs[5] }, DashboardDetail.Summarized);
        _jobs[5].Log.Info("one line");
        var oneLine = DashboardLayout.Build(_jobs, LiveView(showLog: true) with { LogJob = _jobs[5] }, DashboardDetail.Summarized);

        Assert.Equal(empty.Count, oneLine.Count);
    }

    [Fact]
    public void TheLogFollowsTheFirstRunningJobUntilAnotherIsPicked()
    {
        var focus = new LogFocus();

        Assert.Same(_jobs[4], focus.Current(_jobs));
        Assert.True(LogFocus.CanSwitch(_jobs));

        focus.Move(_jobs, 1);
        Assert.Same(_jobs[5], focus.Current(_jobs));

        // Only started jobs can be picked: past App6 (running) it wraps to App1, skipping the waiting ones.
        focus.Move(_jobs, 1);
        Assert.Same(_jobs[0], focus.Current(_jobs));
        focus.Move(_jobs, -1);
        Assert.Same(_jobs[5], focus.Current(_jobs));
    }

    private static DashboardView LiveView(bool showLog = false) => new(Frame: 0, showLog, LogLines: 3, Width, Live: true);

    private static string Plain(string markup) => Markup.Remove(markup);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The batch didn't reach the expected state.");
            await Task.Delay(10);
        }
    }

    private static PublishRequest Request(string name)
    {
        var project = new ProjectInfo { Name = name, FilePath = Path.Combine(Path.GetTempPath(), name, $"{name}.csproj"), Kind = ProjectKind.Web };
        var profile = new PublishProfile("Folder");
        profile[ProfileProperties.WebPublishMethod] = "FileSystem";
        return new PublishRequest(project, profile);
    }

    /// <summary>Publishes with one step that waits until the test opens the project's gate.</summary>
    private sealed class GatedTarget(Dictionary<string, TaskCompletionSource> gates) : IPublishTarget
    {
        public PublishTargetType Type => PublishTargetType.Folder;

        public bool CanPublish(PublishProfile profile) => true;

        public bool RequiresPassword(PublishProfile profile) => false;

        public IReadOnlyList<PublishStep> CreateSteps(PublishRequest request) => [new GatedStep(gates, request.Project.Name)];
    }

    private sealed class GatedStep(Dictionary<string, TaskCompletionSource> gates, string project) : PublishStep("Build and publish")
    {
        public override Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken) => gates[project].Task;
    }
}
