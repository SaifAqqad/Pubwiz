using Pubwiz.Core.Publishing;
using Pubwiz.Core.Tests.TestHelpers;

namespace Pubwiz.Core.Tests.Publishing;

public class PublishOrderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void ToSteps_WithoutOrders_IsOneAfterAnother()
    {
        Assert.Equal([1, 2, 3], PublishOrder.ToSteps([null, null, null]));
    }

    [Fact]
    public void ToSteps_WithoutOrdersButParallel_IsOneStep()
    {
        // Presets saved before orders existed: "parallel": 2 published the whole list, 2 at a time.
        Assert.Equal([1, 1, 1], PublishOrder.ToSteps([null, null, null], parallel: 2));
    }

    [Fact]
    public void ToSteps_RemovesGaps()
    {
        Assert.Equal([2, 1, 2], PublishOrder.ToSteps([5, 1, 5]));
    }

    [Fact]
    public void ToSteps_PutsProjectsWithoutAnOrderLastOneAtATime()
    {
        Assert.Equal([2, 3, 1, 4], PublishOrder.ToSteps([2, null, 1, null]));
    }

    [Fact]
    public async Task RunAsync_PublishesTheSameStepTogetherAndTheNextStepAfterIt()
    {
        var events = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;

        var step = new DelegateStep("Build", async (context, _) =>
        {
            var name = context.Request.Project.Name;
            events.Enqueue($"start {name}");
            if (context.Request.Order == 1)
            {
                // Both jobs of step 1 must be running at the same time for this to finish.
                if (Interlocked.Increment(ref started) == 2)
                {
                    bothStarted.SetResult();
                }

                await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }

            events.Enqueue($"end {name}");
        });

        var runner = new PublishRunner([new FakeTarget(step)]);
        var jobs = new[] { ("Web", 2), ("Api", 1), ("Worker", 1) }
            .Select(p => runner.CreateJob(Named(p.Item1) with { Order = p.Item2 }))
            .ToList();

        await runner.RunAsync(jobs, maxParallelism: int.MaxValue, Token);

        Assert.All(jobs, job => Assert.Equal(JobStatus.Succeeded, job.Status));
        var list = events.ToList();
        Assert.Equal("start Web", list[^2]);
        Assert.Equal("end Web", list[^1]);
    }

    private static PublishRequest Named(string name)
    {
        var request = TestRequests.Create();
        return request with { Project = new Pubwiz.Core.Discovery.ProjectInfo { Name = name, FilePath = request.Project.FilePath, Kind = request.Project.Kind } };
    }
}
