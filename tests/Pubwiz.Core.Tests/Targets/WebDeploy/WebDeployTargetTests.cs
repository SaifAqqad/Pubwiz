using Pubwiz.Core.Discovery;
using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Targets;
using Pubwiz.Core.Targets.Azure;
using Pubwiz.Core.Targets.Azure.Kudu;
using Pubwiz.Core.Targets.WebDeploy;

namespace Pubwiz.Core.Tests.Targets.WebDeploy;

public sealed class WebDeployTargetTests
{
    private static readonly DotnetCli Dotnet = new(new ProcessRunner(), "dotnet");

    private static WebDeployTarget Target(bool installed) => new(Dotnet, new MsDeployCli(new ProcessRunner(), installed ? @"C:\msdeploy.exe" : null));

    private static PublishProfile Profile(string serviceUrl = "iis.contoso.local", string? site = "Default Web Site/api")
    {
        var profile = new PublishProfile("Iis");
        profile[ProfileProperties.WebPublishMethod] = "MSDeploy";
        profile[ProfileProperties.MSDeployServiceURL] = serviceUrl;
        profile[ProfileProperties.DeployIisAppPath] = site;
        profile[ProfileProperties.UserName] = "deploy";
        return profile;
    }

    private static PublishRequest Request(PublishProfile profile) =>
        new(new ProjectInfo { Name = "App", FilePath = Path.Combine(Path.GetTempPath(), "App", "App.csproj"), Kind = ProjectKind.Web }, profile) { Password = "s3cret" };

    [Fact]
    public void FindTarget_LeavesAzureWebDeployProfilesToKudu()
    {
        var credentials = new KuduCredentialsProvider(new AzCli(new ProcessRunner()));
        var runner = new PublishRunner([new AzureAppServiceTarget(Dotnet, new KuduClient(new HttpClient()), credentials), Target(installed: true)]);

        Assert.Equal(PublishTargetType.WebDeploy, runner.FindTarget(Profile())?.Type);
        Assert.Equal(PublishTargetType.AzureAppService, runner.FindTarget(Profile("contoso.scm.azurewebsites.net:443"))?.Type);
    }

    [Fact]
    public void CreateJob_FailsStraightAwayWithoutMsDeploy()
    {
        var job = new PublishRunner([Target(installed: false)]).CreateJob(Request(Profile()));

        Assert.Equal(PublishTargetType.WebDeploy, job.TargetType);
        Assert.Empty(job.Steps);
        Assert.Equal(MsDeployLocator.MissingReason, job.Error);
    }

    [Fact]
    public void CreateJob_ReportsAnIncompleteProfile()
    {
        var job = new PublishRunner([Target(installed: true)]).CreateJob(Request(Profile(site: null)));

        Assert.Empty(job.Steps);
        Assert.Contains(ProfileProperties.DeployIisAppPath, job.Error);
    }

    [Fact]
    public void CreateJob_BuildsThenSyncs()
    {
        var job = new PublishRunner([Target(installed: true)]).CreateJob(Request(Profile()));

        Assert.Null(job.Error);
        Assert.Equal(["Build and publish", "Deploy (Web Deploy)", "Done"], job.Steps.Select(s => s.Name));
    }

    [Fact]
    public void RequiresPassword_OnlyWithAUserName()
    {
        var windowsAccount = Profile();
        windowsAccount[ProfileProperties.UserName] = null;

        Assert.True(Target(installed: true).RequiresPassword(Profile()));
        Assert.False(Target(installed: true).RequiresPassword(windowsAccount));
    }

    [Fact]
    public void CreateSyncOptions_ReadsTheVisualStudioProperties()
    {
        var profile = Profile();
        profile[ProfileProperties.AllowUntrustedCertificate] = "True";
        profile[ProfileProperties.SkipExtraFilesOnServer] = "true";
        profile[ProfileProperties.EnableMSDeployAppOffline] = "true";
        profile[ProfileProperties.AuthType] = "NTLM";
        var request = Request(profile);

        var options = WebDeployTarget.CreateSyncOptions(request, WebDeployProfile.GetEndpoint(profile), "/staging");

        Assert.Equal("/staging", options.SourceDirectory);
        Assert.Equal("Default Web Site/api", options.Destination.IisPath);
        Assert.Equal("deploy", options.UserName);
        Assert.Equal("s3cret", options.Password);
        Assert.Equal("NTLM", options.EffectiveAuthType);
        Assert.True(options.AllowUntrustedCertificate);
        Assert.True(options.KeepExtraFiles);
        Assert.True(options.TakeAppOffline);
    }

    [Fact]
    public void Catalog_KeepsWebDeploySelectable_WithAWarningWhereMsDeployIsMissing()
    {
        var webDeploy = PublishTargetCatalog.For(ProjectKind.Web).Single(t => t.Type == PublishTargetType.WebDeploy);

        Assert.True(webDeploy.IsAvailable);
        Assert.Equal(MsDeployLocator.Find() is null, webDeploy.Warning is not null);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal("needs Windows", webDeploy.Warning);
        }
    }

    [Fact]
    public void Alternatives_DependOnTheServer()
    {
        Assert.Equal(
            [PublishTargetType.AzureAppService, PublishTargetType.Ftp, PublishTargetType.Sftp, PublishTargetType.ZipFile],
            WebDeployAlternatives.SuggestFor(endpoint: null));
        Assert.Equal([PublishTargetType.Ftp, PublishTargetType.Sftp, PublishTargetType.ZipFile],
            WebDeployAlternatives.SuggestFor(WebDeployProfile.GetEndpoint(Profile())));
        Assert.Equal([PublishTargetType.AzureAppService],
            WebDeployAlternatives.SuggestFor(WebDeployProfile.GetEndpoint(Profile("contoso.scm.azurewebsites.net:443"))));
    }

    [Fact]
    public void Alternatives_AreOnlyNeededForOnPremisesProfilesWithoutMsDeploy()
    {
        var folder = new PublishProfile("Folder");
        folder[ProfileProperties.WebPublishMethod] = "FileSystem";

        Assert.Equal(MsDeployLocator.Find() is null, WebDeployAlternatives.AreNeeded(Profile()));
        Assert.False(WebDeployAlternatives.AreNeeded(Profile("contoso.scm.azurewebsites.net:443")));
        Assert.False(WebDeployAlternatives.AreNeeded(folder));
    }

    [Fact]
    public void TryGetEndpoint_IsNullForAnIncompleteProfile()
    {
        Assert.Null(WebDeployAlternatives.TryGetEndpoint(Profile(site: null)));
        Assert.Equal("iis.contoso.local", WebDeployAlternatives.TryGetEndpoint(Profile())?.ServiceUrl.Host);
    }
}
