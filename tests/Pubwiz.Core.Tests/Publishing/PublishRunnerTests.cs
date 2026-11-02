using Pubwiz.Core.Publishing;
using Pubwiz.Core.Tests.TestHelpers;

namespace Pubwiz.Core.Tests.Publishing;

public class PublishRunnerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RunAsync_RunsAllStepsInOrder()
    {
        var order = new List<string>();
        var first = new DelegateStep("First", (_, _) => { order.Add("First"); return Task.CompletedTask; });
        var second = new DelegateStep("Second", (_, _) => { order.Add("Second"); return Task.CompletedTask; });
        var runner = new PublishRunner([new FakeTarget(first, second)]);
        var job = runner.CreateJob(TestRequests.Create());

        await runner.RunAsync([job], maxParallelism: 1, Token);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(["First", "Second"], order);
        Assert.All(job.Steps, step => Assert.Equal(StepStatus.Succeeded, step.Status));
        Assert.NotNull(job.Duration);
    }

    [Fact]
    public async Task RunAsync_SkipsRemainingStepsAfterAFailure()
    {
        var last = DelegateStep.Succeeds("Upload");
        var runner = new PublishRunner([new FakeTarget(DelegateStep.Fails("Build", "error CS1002: ; expected"), last)]);
        var job = runner.CreateJob(TestRequests.Create());

        await runner.RunAsync([job], maxParallelism: 1, Token);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("error CS1002: ; expected", job.Error);
        Assert.Equal("Build", job.FailedStep?.Name);
        Assert.Equal(StepStatus.Skipped, last.Status);
        Assert.False(last.Ran);
    }

    [Fact]
    public async Task RunAsync_MarksJobsCancelledWhenTokenIsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var waits = new DelegateStep("Wait", async (_, ct) =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(Timeout.Infinite, ct);
        });
        var runner = new PublishRunner([new FakeTarget(waits, DelegateStep.Succeeds("After"))]);
        var job = runner.CreateJob(TestRequests.Create());

        await runner.RunAsync([job], maxParallelism: 1, cancellation.Token);

        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Equal(StepStatus.Skipped, job.Steps[1].Status);
    }

    [Theory]
    [InlineData(2, 2, true)]
    [InlineData(2, 1, false)] // one after another
    [InlineData(1, 4, false)] // a single job
    public async Task RunAsync_GivesEachJobItsOwnBuildFolderOnlyWhenJobsRunInParallel(int jobCount, int maxParallelism, bool isolated)
    {
        var buildDirectories = new System.Collections.Concurrent.ConcurrentBag<string?>();
        var records = new DelegateStep("Build", (context, _) => { buildDirectories.Add(context.BuildDirectory); return Task.CompletedTask; });
        var runner = new PublishRunner([new FakeTarget(records)]);
        var jobs = Enumerable.Range(0, jobCount).Select(_ => runner.CreateJob(TestRequests.Create())).ToList();

        await runner.RunAsync(jobs, maxParallelism, Token);

        var expected = isolated ? BuildIsolation.GetBuildDirectory(jobs[0].Request) : null;
        Assert.All(buildDirectories, directory => Assert.Equal(expected, directory));
    }

    [Fact]
    public void GetBuildDirectory_IsPerProfileAndOutsideTheStagingFolder()
    {
        var request = TestRequests.Create();

        var directory = BuildIsolation.GetBuildDirectory(request);

        Assert.Equal(Path.Combine(request.Project.Directory, "obj", "pubwiz-build", "Test"), directory);
        Assert.False(directory.StartsWith(Core.Publishing.Steps.StagingPublish.GetStagingDirectory(request), StringComparison.Ordinal));
    }

    [Fact]
    public void CreateJob_FailsAPreviewForATargetThatCantPreview()
    {
        var runner = new PublishRunner([new FakeTarget(DelegateStep.Succeeds("Build"))]);

        var job = runner.CreateJob(TestRequests.Create() with { WhatIf = true });

        Assert.Empty(job.Steps);
        Assert.Contains("--what-if", job.Error);
    }

    [Fact]
    public async Task CreateJob_FailsJobsWithUnsupportedProfilesWithoutStoppingOthers()
    {
        var runner = new PublishRunner([new FakeTarget(DelegateStep.Succeeds("Build"))]);
        var unsupported = runner.CreateJob(TestRequests.Create(method: "Package"));
        var supported = runner.CreateJob(TestRequests.Create());

        await runner.RunAsync([unsupported, supported], maxParallelism: 2, Token);

        Assert.Null(unsupported.TargetType);
        Assert.Equal(JobStatus.Failed, unsupported.Status);
        Assert.Equal(JobStatus.Succeeded, supported.Status);
    }

    [Fact]
    public async Task RunAsync_ReportsUnexpectedExceptionsWithTheirType()
    {
        var crashes = new DelegateStep("Crash", (_, _) => throw new InvalidOperationException("boom"));
        var runner = new PublishRunner([new FakeTarget(crashes)]);
        var job = runner.CreateJob(TestRequests.Create());

        await runner.RunAsync([job], maxParallelism: 1, Token);

        Assert.Equal("InvalidOperationException: boom", job.Error);
    }

    [Fact]
    public void CreateJob_MasksThePasswordInTheLog()
    {
        var runner = new PublishRunner([new FakeTarget()]);
        var job = runner.CreateJob(TestRequests.Create(password: "hunter2-secret"));

        job.Log.Info("msdeploy -dest:password=hunter2-secret");

        Assert.Equal("msdeploy -dest:password=********", job.Log.Snapshot().Single().Text);
    }
}
