using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Tool.Rendering;
using Pubwiz.Tool.Wizard;
using Spectre.Console;

namespace Pubwiz.Tool.Tests.Rendering;

public class PublishPlanTests
{
    [Theory]
    [InlineData(new[] { 1, 1, 1 }, null, "All 3 at the same time")]
    [InlineData(new[] { 1, 1, 1 }, 2, "All 3 at the same time (2 at a time at most)")]
    [InlineData(new[] { 1, 2, 3 }, null, "One after another: A, then B, then C")]
    [InlineData(new[] { 1, 1, 2 }, null, "A and B at the same time, then C")]
    [InlineData(new[] { 2, 1, 1 }, null, "B and C at the same time, then A")]
    public void Describe_SaysWhatHappensInWhichOrder(int[] steps, int? parallel, string expected)
    {
        var names = steps.Select((step, i) => (step, ((char)('A' + i)).ToString())).ToList();

        Assert.Equal(expected, PublishPlan.Describe(names, parallel));
    }

    [Fact]
    public void Connector_JoinsTheRowsOfAStep()
    {
        Assert.Equal("─", PublishPlan.Connector(0, 1, unicode: true));
        Assert.Equal(["┌", "├", "└"], Enumerable.Range(0, 3).Select(i => PublishPlan.Connector(i, 3, unicode: true)));
    }

    [Theory]
    [InlineData(new[] { 1, 1, 1 }, null, "Publish 3 projects, all at the same time?")]
    [InlineData(new[] { 1, 1, 1 }, 2, "Publish 3 projects, 2 at a time?")]
    [InlineData(new[] { 1, 2, 3 }, null, "Publish 3 projects, one after another?")]
    [InlineData(new[] { 1, 1, 2 }, null, "Publish 3 projects in 2 steps?")]
    public void ConfirmQuestion_SaysHowTheBatchIsPublished(int[] orders, int? parallel, string expected)
    {
        var batch = new PublishBatch(orders.Select((order, i) => new PublishSelection(Project($"P{i}"), new PublishProfile("Prod")) { Order = order }).ToList(), Parallel: parallel);

        Assert.Equal(expected, ConfirmPage.Question(batch));
    }

    [Fact]
    public void Dashboard_ShowsAHeadingPerStep()
    {
        var runner = new PublishRunner([]);
        var jobs = new[] { ("Api", 1), ("Worker", 1), ("Web", 2) }
            .Select(p => runner.CreateJob(new PublishRequest(Project(p.Item1), new PublishProfile("Prod")) { Order = p.Item2 }))
            .ToList();
        var view = new DashboardView(Frame: 0, ShowLog: false, LogLines: 3, Width: 80, Live: true);

        var lines = DashboardLayout.Build(jobs, view, DashboardDetail.Full).Select(Markup.Remove).ToList();

        Assert.Contains("Step 1 of 2 · 2 at the same time", lines);
        Assert.Contains("Step 2 of 2", lines);
        Assert.True(lines.IndexOf("Step 2 of 2") > lines.FindIndex(line => line.Contains(" Worker ")));
        Assert.StartsWith("step 1/2 · ", DashboardLayout.ProgressLine(jobs));
    }

    private static ProjectInfo Project(string name) =>
        new() { Name = name, FilePath = Path.Combine(Path.GetTempPath(), name, $"{name}.csproj"), Kind = ProjectKind.Web };
}
