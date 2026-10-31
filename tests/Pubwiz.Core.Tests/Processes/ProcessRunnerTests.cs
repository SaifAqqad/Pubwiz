using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Processes;

namespace Pubwiz.Core.Tests.Processes;

public class ProcessRunnerTests
{
    private readonly ProcessRunner _runner = new();

    private static string Dotnet => DotnetLocator.Find() ?? throw new InvalidOperationException("dotnet not found");

    [Fact]
    public async Task RunAsync_CapturesOutputAndExitCode()
    {
        var result = await _runner.RunAsync(new ProcessSpec(Dotnet, ["--version"]), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Matches(@"^\d+\.\d+\.\d+", result.Output.Trim());
    }

    [Fact]
    public async Task RunAsync_StreamsEachLine()
    {
        var lines = new List<ProcessOutputLine>();

        var result = await _runner.RunAsync(new ProcessSpec(Dotnet, ["--list-sdks"]), lines.Add, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.False(line.IsError));
    }

    [Fact]
    public async Task RunAsync_ReportsNonZeroExitCode()
    {
        var result = await _runner.RunAsync(new ProcessSpec(Dotnet, ["no-such-command-pubwiz"]), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RunAsync_ThrowsWhenExecutableIsMissing()
    {
        var spec = new ProcessSpec("pubwiz-missing-executable", []);

        await Assert.ThrowsAsync<ProcessStartException>(() => _runner.RunAsync(spec, cancellationToken: TestContext.Current.CancellationToken));
    }
}
