using Pubwiz.Core.Discovery;
using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Targets.FileTransfer;
using Pubwiz.Core.Targets.ServerLogins;
using Pubwiz.Core.Tests.TestHelpers;

namespace Pubwiz.Core.Tests.Targets.FileTransfer;

public sealed class FileTransferTargetTests : IDisposable
{
    private static readonly DotnetCli Dotnet = new(new ProcessRunner(), "dotnet");

    private readonly TempDirectory _temp = new();
    private readonly ServerCredentialStore _servers;

    public FileTransferTargetTests() => _servers = new ServerCredentialStore(new MemorySecretStore(), Path.Combine(_temp.Path, "servers.json"));

    public void Dispose() => _temp.Dispose();

    private FileTransferTarget Target(bool sftp) => new(Dotnet, _servers, sftp);

    private static readonly ProjectInfo Project = new() { Name = "Shop", FilePath = "/src/Shop/Shop.csproj", Kind = ProjectKind.Web, TargetFrameworks = ["net10.0"] };

    private static PublishRequest Request(string url, string? preDeploy = null, string? postDeploy = null)
    {
        var profile = new PublishProfile("Server");
        profile[ProfileProperties.PublishUrl] = url;
        profile[ProfileProperties.UserName] = "deploy";
        profile[ProfileProperties.PubwizSshPreDeployCommand] = preDeploy;
        profile[ProfileProperties.PubwizSshPostDeployCommand] = postDeploy;
        return new PublishRequest(Project, profile);
    }

    [Fact]
    public void Sftp_RunsTheCommandsAroundTheUpload()
    {
        var steps = Target(sftp: true).CreateSteps(
            Request("sftp://server/var/www/shop", "sudo systemctl stop $(ProjectName)", "sudo systemctl restart $(ProjectName)"));

        Assert.Equal(["Build and publish", "Run pre-deploy command (SSH)", "Upload (SFTP)", "Run post-deploy command (SSH)", "Done"], steps.Select(s => s.Name));
        Assert.Equal("sudo systemctl stop Shop", Assert.IsType<SshCommandStep>(steps[1]).Command);
        Assert.Equal("sudo systemctl restart Shop", Assert.IsType<SshCommandStep>(steps[3]).Command);
    }

    [Fact]
    public void Sftp_LeavesShellSyntaxAlone()
    {
        var steps = Target(sftp: true).CreateSteps(Request("sftp://server/app", postDeploy: "echo $(whoami) && $env:PATH"));

        Assert.Equal("echo $(whoami) && $env:PATH", Assert.IsType<SshCommandStep>(steps[2]).Command);
    }

    [Fact]
    public void Sftp_WithoutCommands_OnlyUploads()
    {
        var steps = Target(sftp: true).CreateSteps(Request("sftp://server/app"));

        Assert.Equal(["Build and publish", "Upload (SFTP)", "Done"], steps.Select(s => s.Name));
    }

    [Fact]
    public void Ftp_IgnoresSshCommands()
    {
        var steps = Target(sftp: false).CreateSteps(Request("ftp://server/app", "stop", "start"));

        Assert.Equal(["Build and publish", "Upload (FTP)", "Done"], steps.Select(s => s.Name));
    }

    [Theory]
    [InlineData("ftp://Server/site/wwwroot", false)]
    [InlineData("sftp://server:2222/var/www", true)]
    public async Task ServerLogin_ReplacesTheProfilesLogin(string url, bool sftp)
    {
        await _servers.SaveAsync("server", "shared", "p@ss", Token);
        var request = Request(url);

        Assert.False(Target(sftp).RequiresPassword(request.Profile));
        Assert.Equal("shared", Target(sftp).FindServerLogin(request.Profile)?.UserName);
    }

    [Fact]
    public async Task ServerLogin_ForAnotherServer_IsNotUsed()
    {
        await _servers.SaveAsync("other-server", "shared", "p@ss", Token);

        Assert.Null(Target(sftp: false).FindServerLogin(Request("ftp://server/app").Profile));
        Assert.True(Target(sftp: false).RequiresPassword(Request("ftp://server/app").Profile));
    }

    [Fact]
    public async Task ServerLogin_WithoutItsPassword_FailsBeforeConnecting()
    {
        await _servers.SaveAsync("server", "shared", "p@ss", Token);
        var withoutPasswords = new ServerCredentialStore(new MemorySecretStore(), _servers.FilePath);
        var target = new FileTransferTarget(Dotnet, withoutPasswords, sftp: true);
        var request = Request("sftp://server/app", preDeploy: "systemctl stop shop");
        var context = new PublishContext(new PublishRunner([target]).CreateJob(request)) { OutputDirectory = _temp.Path };

        var error = await Assert.ThrowsAsync<PublishStepException>(() => target.CreateSteps(request)[1].ExecuteAsync(context, Token));

        Assert.Contains("servers set server", error.Message);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
