using Pubwiz.Core.Discovery;
using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Targets.FileTransfer;

namespace Pubwiz.Core.Tests.Targets.FileTransfer;

public class FileTransferTargetTests
{
    private static readonly DotnetCli Dotnet = new(new ProcessRunner(), "dotnet");

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
        var steps = new FileTransferTarget(Dotnet, sftp: true).CreateSteps(
            Request("sftp://server/var/www/shop", "sudo systemctl stop $(ProjectName)", "sudo systemctl restart $(ProjectName)"));

        Assert.Equal(["Build and publish", "Run pre-deploy command (SSH)", "Upload (SFTP)", "Run post-deploy command (SSH)", "Done"], steps.Select(s => s.Name));
        Assert.Equal("sudo systemctl stop Shop", Assert.IsType<SshCommandStep>(steps[1]).Command);
        Assert.Equal("sudo systemctl restart Shop", Assert.IsType<SshCommandStep>(steps[3]).Command);
    }

    [Fact]
    public void Sftp_LeavesShellSyntaxAlone()
    {
        var steps = new FileTransferTarget(Dotnet, sftp: true).CreateSteps(Request("sftp://server/app", postDeploy: "echo $(whoami) && $env:PATH"));

        Assert.Equal("echo $(whoami) && $env:PATH", Assert.IsType<SshCommandStep>(steps[2]).Command);
    }

    [Fact]
    public void Sftp_WithoutCommands_OnlyUploads()
    {
        var steps = new FileTransferTarget(Dotnet, sftp: true).CreateSteps(Request("sftp://server/app"));

        Assert.Equal(["Build and publish", "Upload (SFTP)", "Done"], steps.Select(s => s.Name));
    }

    [Fact]
    public void Ftp_IgnoresSshCommands()
    {
        var steps = new FileTransferTarget(Dotnet, sftp: false).CreateSteps(Request("ftp://server/app", "stop", "start"));

        Assert.Equal(["Build and publish", "Upload (FTP)", "Done"], steps.Select(s => s.Name));
    }
}
