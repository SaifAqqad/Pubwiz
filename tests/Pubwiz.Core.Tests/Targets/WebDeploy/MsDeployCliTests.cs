using Pubwiz.Core.Infrastructure;
using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Targets.WebDeploy;
using Pubwiz.WebDeploy;

namespace Pubwiz.Core.Tests.Targets.WebDeploy;

public sealed class MsDeployCliTests
{
    private static readonly string AppFolder = Path.Combine(Path.GetTempPath(), "app");

    private static MsDeploySyncOptions Options(string iisPath = "PubwizTest") =>
        new(AppFolder + Path.DirectorySeparatorChar, WebDeployEndpoint.Create("iis.contoso.local", iisPath))
        {
            UserName = "deploy",
            Password = "s3cret"
        };

    [Fact]
    public void BuildSyncCommandLine_QuotesEveryValue()
    {
        var commandLine = MsDeployCli.BuildSyncCommandLine(Options("Default Web Site/api"));

        Assert.Equal(
            $"-verb:sync -source:iisApp=\"{AppFolder}\" " +
            "-dest:iisApp=\"Default Web Site/api\",computerName=\"https://iis.contoso.local:8172/msdeploy.axd?site=Default%20Web%20Site\"," +
            "userName=\"deploy\",password=\"s3cret\",authType=\"Basic\"",
            commandLine);
    }

    [Fact]
    public void BuildSyncCommandLine_UsesTheWindowsAccountWithoutAUserName()
    {
        var commandLine = MsDeployCli.BuildSyncCommandLine(Options() with { UserName = null, Password = null });

        Assert.DoesNotContain("userName", commandLine);
        Assert.DoesNotContain("password", commandLine);
        Assert.Contains("authType=\"NTLM\"", commandLine);
    }

    [Fact]
    public void BuildSyncCommandLine_AddsTheChosenRules()
    {
        var options = Options() with { AllowUntrustedCertificate = true, KeepExtraFiles = true, TakeAppOffline = true, Verbose = true };

        Assert.EndsWith(" -allowUntrusted -enableRule:DoNotDeleteRule -enableRule:AppOffline -verbose", MsDeployCli.BuildSyncCommandLine(options));
    }

    [Theory]
    [InlineData("pa\"ss")]
    [InlineData("pa'ss")]
    [InlineData("pass\\")]
    public void BuildSyncCommandLine_RejectsValuesMsDeployCantRead_WithoutShowingThem(string password)
    {
        var error = Assert.Throws<MsDeployException>(() => MsDeployCli.BuildSyncCommandLine(Options() with { Password = password }));

        Assert.Contains("password", error.Message);
        Assert.DoesNotContain(password, error.Message);
    }

    [Theory]
    [InlineData("pa,ss word")]
    [InlineData("p=a;s%s")]
    public void BuildSyncCommandLine_KeepsCommasAndSpacesInsideTheQuotes(string password)
    {
        Assert.Contains($",password=\"{password}\",", MsDeployCli.BuildSyncCommandLine(Options() with { Password = password }));
    }

    [Fact]
    public void DescribeFailure_UsesTheErrorCodeAndExplanation()
    {
        var result = new ProcessResult(-1, "Info: Using ID 'x' for connections to the remote server.\n", """
            Error Code: ERROR_USER_UNAUTHORIZED
            More Information: Connected to the remote computer ("iis") using the Web Management Service, but could not authorize.  Learn more at: https://go.microsoft.com/fwlink/?LinkId=221672#ERROR_USER_UNAUTHORIZED.
            Error: The remote server returned an error: (401) Unauthorized.
            Error count: 1.
            """);

        Assert.Equal("ERROR_USER_UNAUTHORIZED: Connected to the remote computer (\"iis\") using the Web Management Service, but could not authorize.",
            MsDeployCli.DescribeFailure(result));
    }

    [Fact]
    public void DescribeFailure_FallsBackToTheErrorLine()
    {
        var result = new ProcessResult(-1, string.Empty, "Error: The provider 'iisApp' does not support the 'ong' setting.\nError count: 1.\n");

        Assert.Equal("The provider 'iisApp' does not support the 'ong' setting.", MsDeployCli.DescribeFailure(result));
    }

    [Fact]
    public void DescribeFailure_FallsBackToTheExitCode()
    {
        Assert.Equal("msdeploy.exe failed with exit code 5", MsDeployCli.DescribeFailure(new ProcessResult(5, string.Empty, string.Empty)));
    }

    [Fact]
    public async Task SyncAsync_PassesTheRawCommandLine()
    {
        var runner = new RecordingRunner(new ProcessResult(0, "Total changes: 1", string.Empty));
        var cli = new MsDeployCli(runner, @"C:\msdeploy.exe");

        await cli.SyncAsync(Options(), _ => { }, TestContext.Current.CancellationToken);

        Assert.Empty(runner.Spec!.Arguments);
        Assert.Equal(MsDeployCli.BuildSyncCommandLine(Options()), runner.Spec.CommandLine);
    }

    [Fact]
    public async Task SyncAsync_ThrowsTheReasonWhenItFails()
    {
        var cli = new MsDeployCli(new RecordingRunner(new ProcessResult(-1, string.Empty, "Error: nope\n")), @"C:\msdeploy.exe");

        var error = await Assert.ThrowsAsync<MsDeployException>(() => cli.SyncAsync(Options(), _ => { }, TestContext.Current.CancellationToken));
        Assert.Equal("nope", error.Message);
    }

    [Fact]
    public async Task SyncAsync_ReportsTheErrorCode()
    {
        const string output = "Error Code: ERROR_FILE_IN_USE\nMore Information: Web Deploy cannot modify the file 'AspNetCore.dll' on the destination because it is locked by an external process.\nError count: 1.\n";
        var cli = new MsDeployCli(new RecordingRunner(new ProcessResult(-1, string.Empty, output)), @"C:\msdeploy.exe");

        var error = await Assert.ThrowsAsync<MsDeployException>(() => cli.SyncAsync(Options(), _ => { }, TestContext.Current.CancellationToken));

        Assert.Equal(MsDeployException.FileInUse, error.ErrorCode);
        Assert.StartsWith("ERROR_FILE_IN_USE: Web Deploy cannot modify", error.Message);
    }

    [Fact]
    public async Task SyncAsync_NeedsMsDeploy()
    {
        var cli = new MsDeployCli(new RecordingRunner(new ProcessResult(0, string.Empty, string.Empty)), executablePath: null);

        await Assert.ThrowsAsync<PrerequisiteMissingException>(() => cli.SyncAsync(Options(), _ => { }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Find_OnlyFindsMsDeployOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Null(MsDeployLocator.Find());
            Assert.Contains("only runs on Windows", MsDeployLocator.MissingReason);
        }
    }

    /// <summary>Records the process it was asked to run and returns a fixed result.</summary>
    private sealed class RecordingRunner(ProcessResult result) : IProcessRunner
    {
        public ProcessSpec? Spec { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessSpec spec, Action<ProcessOutputLine>? onOutput = null, CancellationToken cancellationToken = default)
        {
            Spec = spec;
            return Task.FromResult(result);
        }

        public Task<int> RunInteractiveAsync(ProcessSpec spec, CancellationToken cancellationToken = default) => Task.FromResult(result.ExitCode);
    }
}
