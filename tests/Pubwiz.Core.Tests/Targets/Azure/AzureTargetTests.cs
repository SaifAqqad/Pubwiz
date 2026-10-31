using Pubwiz.Core.Discovery;
using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Targets;
using Pubwiz.Core.Targets.Azure;
using Pubwiz.Core.Targets.Azure.Kudu;
using Pubwiz.Core.Targets.FileTransfer;
using Pubwiz.Core.Targets.Folder;
using Pubwiz.Core.Tests.TestHelpers;
using System.Net;
using System.Xml.Linq;

namespace Pubwiz.Core.Tests.Targets.Azure;

public sealed class AzureTargetTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeHttpHandler _kudu = new();
    private readonly DotnetCli _dotnet = new(new ProcessRunner(), DotnetLocator.Find()!);

    private PublishRunner CreateRunner()
    {
        var kudu = new KuduClient(new HttpClient(_kudu)) { PollInterval = TimeSpan.Zero };
        var credentials = new KuduCredentialsProvider(new AzCli(new ProcessRunner()));
        return new PublishRunner(
        [
            new FolderTarget(_dotnet),
            new FileTransferTarget(_dotnet, sftp: false),
            new AzureWebJobTarget(_dotnet, kudu, credentials),
            new AzureAppServiceTarget(_dotnet, kudu, credentials)
        ]);
    }

    private static PublishProfile Profile(string method, string? publishUrl = null, string? serviceUrl = null, string? webJob = null)
    {
        var profile = new PublishProfile("Azure");
        profile[ProfileProperties.WebPublishMethod] = method;
        profile[ProfileProperties.PublishUrl] = publishUrl;
        profile[ProfileProperties.MSDeployServiceURL] = serviceUrl;
        profile[ProfileProperties.DeployIisAppPath] = serviceUrl is null ? null : "contoso";
        profile[ProfileProperties.WebJobName] = webJob;
        profile[ProfileProperties.UserName] = "$contoso";
        return profile;
    }

    [Theory]
    [InlineData("https://contoso.scm.azurewebsites.net/")]
    [InlineData("contoso.scm.azurewebsites.net:443")]
    public void GetScmUrl_AcceptsBothUrlForms(string publishUrl)
    {
        Assert.Equal("https://contoso.scm.azurewebsites.net/", AzureProfile.GetScmUrl(Profile("ZipDeploy", publishUrl)).AbsoluteUri);
    }

    [Fact]
    public void GetSiteUrl_FallsBackToTheHostWithoutScm()
    {
        Assert.Equal("https://contoso.azurewebsites.net", AzureProfile.GetSiteUrl(Profile("ZipDeploy", "https://contoso.scm.azurewebsites.net/")));
    }

    [Fact]
    public void FindTarget_SendsEachProfileToTheRightTarget()
    {
        var runner = CreateRunner();

        Assert.Equal(PublishTargetType.AzureAppService, runner.FindTarget(Profile("ZipDeploy", "https://contoso.scm.azurewebsites.net/"))?.Type);
        Assert.Equal(PublishTargetType.AzureWebJob, runner.FindTarget(Profile("ZipDeploy", "https://contoso.scm.azurewebsites.net/", webJob: "job"))?.Type);
        Assert.Equal(PublishTargetType.AzureAppService, runner.FindTarget(Profile("MSDeploy", serviceUrl: "contoso.scm.azurewebsites.net:443"))?.Type);
        Assert.Null(runner.FindTarget(Profile("MSDeploy", serviceUrl: "iis.contoso.local")));
    }

    [Fact]
    public void ImportedPublishSettings_AreAllPublishable()
    {
        var settings = XDocument.Parse("""
            <publishData>
              <publishProfile profileName="contoso - Web Deploy" publishMethod="MSDeploy" publishUrl="contoso.scm.azurewebsites.net:443" msdeploySite="contoso" userName="$contoso" userPWD="x" destinationAppUrl="https://contoso.azurewebsites.net" />
              <publishProfile profileName="contoso - FTP" publishMethod="FTP" publishUrl="ftps://waws.ftp.azurewebsites.windows.net/site/wwwroot" userName="contoso\$contoso" userPWD="x" />
              <publishProfile profileName="contoso - Zip Deploy" publishMethod="ZipDeploy" publishUrl="contoso.scm.azurewebsites.net:443" userName="$contoso" userPWD="x" />
            </publishData>
            """);
        var runner = CreateRunner();

        var targets = PublishSettingsImporter.Parse(settings)
            .Select(e => runner.FindTarget(PublishSettingsImporter.ToProfile(e).Profile)?.Type)
            .ToList();

        Assert.Equal([PublishTargetType.AzureAppService, PublishTargetType.Ftp, PublishTargetType.AzureAppService], targets);
    }

    [Fact]
    public void UsesAzCli_WhenThereAreNoPublishingCredentials()
    {
        var profile = Profile("ZipDeploy", "https://contoso.scm.azurewebsites.net/");
        Assert.False(AzureProfile.UsesAzCli(profile));

        profile[ProfileProperties.UserName] = null;
        Assert.True(AzureProfile.UsesAzCli(profile));
    }

    [Fact]
    public async Task AppServiceJob_PublishesZipsAndDeploysThroughKudu()
    {
        _kudu.Respond(HttpStatusCode.Accepted, location: new Uri("/api/deployments/1", UriKind.Relative))
            .Respond(HttpStatusCode.OK, """{"id":"1","status":4}""");

        var job = await RunAsync(Profile("ZipDeploy", "https://contoso.scm.azurewebsites.net/"));

        Assert.Null(job.Error);
        Assert.Equal(["Build and publish", "Package", "Deploy to App Service", "Done"], job.Steps.Select(s => s.Name));
        Assert.Equal("https://contoso.azurewebsites.net", job.Destination);
        Assert.Equal("https://contoso.scm.azurewebsites.net/api/publish?type=zip", _kudu.Requests[0].Url.AbsoluteUri);
        Assert.StartsWith("Basic ", _kudu.Requests[0].Authorization);
    }

    [Fact]
    public async Task WebJobJob_AddsARunScriptAndUploadsToTheWebJobsApi()
    {
        _kudu.Respond(HttpStatusCode.OK, "{}");
        var profile = Profile("ZipDeploy", "https://contoso.scm.azurewebsites.net/", webJob: "nightly");
        profile[ProfileProperties.WebJobType] = "Triggered";
        profile[ProfileProperties.PubwizAzureLinux] = "true";
        profile[ProfileProperties.PubwizWebJobSchedule] = "0 0 2 * * *";

        var job = await RunAsync(profile);

        Assert.Null(job.Error);
        Assert.Equal(HttpMethod.Put, _kudu.Requests[0].Method);
        Assert.Equal("https://contoso.scm.azurewebsites.net/api/triggeredwebjobs/nightly", _kudu.Requests[0].Url.AbsoluteUri);
        Assert.Equal("#!/bin/bash\ndotnet App.dll\n", File.ReadAllText(Path.Combine(job.OutputDirectory!, "run.sh")));
        Assert.Contains("0 0 2 * * *", File.ReadAllText(Path.Combine(job.OutputDirectory!, "settings.job")));
    }

    private async Task<PublishJob> RunAsync(PublishProfile profile)
    {
        var projectPath = _temp.CreateProject("App/App.csproj", properties: "<TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>");
        _temp.CreateFile("App/Program.cs", """System.Console.WriteLine("hi");""");
        var saved = PublishProfileStore.Save(Path.GetDirectoryName(projectPath)!, profile);

        var project = new ProjectInfo { Name = "App", FilePath = projectPath, Kind = ProjectKind.Console, TargetFrameworks = ["net10.0"] };
        var runner = CreateRunner();
        var job = runner.CreateJob(new PublishRequest(project, saved) { Password = "secret" });
        await runner.RunAsync([job], 1, TestContext.Current.CancellationToken);
        return job;
    }

    public void Dispose() => _temp.Dispose();
}
