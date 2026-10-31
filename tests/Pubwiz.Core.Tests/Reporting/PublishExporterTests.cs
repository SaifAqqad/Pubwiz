using Pubwiz.Core.Execution;
using Pubwiz.Core.Reporting;
using Pubwiz.Core.Tests.TestHelpers;
using System.Text.Json;

namespace Pubwiz.Core.Tests.Reporting;

public sealed class PublishExporterTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData(null, "")]
    public void EscapeCsv_FollowsRfc4180(string? value, string expected)
    {
        Assert.Equal(expected, PublishExporter.EscapeCsv(value));
    }

    [Fact]
    public async Task Reports_ContainOneEntryPerJob()
    {
        var runner = new PublishRunner([new FakeTarget(DelegateStep.Fails("Build", "broken, badly"))]);
        var job = runner.CreateJob(TestRequests.Create());
        await runner.RunAsync([job], 1, TestContext.Current.CancellationToken);

        // The folders don't exist yet: the exporter creates them.
        var json = Path.Combine(_temp.Path, "reports", "report.json");
        var csv = Path.Combine(_temp.Path, "ci", "out", "report.csv");
        PublishExporter.WriteJsonReport([job], json);
        PublishExporter.WriteCsvReport([job], csv);

        var entries = JsonDocument.Parse(File.ReadAllText(json)).RootElement;
        Assert.Equal("Failed", entries[0].GetProperty("status").GetString());
        Assert.Equal("Build", entries[0].GetProperty("failedStep").GetString());

        var lines = File.ReadAllLines(csv);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"broken, badly\"", lines[1]);
    }

    [Fact]
    public async Task WriteLogs_WritesOneFilePerJob()
    {
        var runner = new PublishRunner([new FakeTarget(DelegateStep.Succeeds("Build"))]);
        var job = runner.CreateJob(TestRequests.Create());
        await runner.RunAsync([job], 1, TestContext.Current.CancellationToken);

        var files = PublishExporter.WriteLogs([job], Path.Combine(_temp.Path, "logs"));

        var file = Assert.Single(files);
        Assert.EndsWith("App.log", file);
        Assert.Contains(File.ReadAllLines(file), line => line.Contains("[Build] pubwiz: ▶ Build"));
    }

    public void Dispose() => _temp.Dispose();
}
