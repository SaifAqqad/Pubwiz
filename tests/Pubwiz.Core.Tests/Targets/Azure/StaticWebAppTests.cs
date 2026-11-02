using Pubwiz.Core.Discovery;
using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Targets.Azure;
using Pubwiz.Core.Targets.Azure.StaticWebApps;
using Pubwiz.Core.Tests.TestHelpers;
using System.Text.Json.Nodes;

namespace Pubwiz.Core.Tests.Targets.Azure;

public sealed class StaticWebAppTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    [Fact]
    public void FindSiteFolder_IsWwwrootOfABlazorPublish()
    {
        _temp.CreateFile("publish/wwwroot/index.html", "<html></html>");
        _temp.CreateFile("publish/web.config", "<configuration />");

        Assert.Equal(Path.Combine(_temp.Path, "publish", "wwwroot"), StaticWebAppSite.FindSiteFolder(Path.Combine(_temp.Path, "publish")));
    }

    [Fact]
    public void FindSiteFolder_IsTheOutputWhenItHoldsTheSite()
    {
        _temp.CreateFile("site/index.html", "<html></html>");

        Assert.Equal(Path.Combine(_temp.Path, "site"), StaticWebAppSite.FindSiteFolder(Path.Combine(_temp.Path, "site")));
    }

    [Fact]
    public void EnsureConfig_AddsTheBlazorFallback()
    {
        Assert.True(StaticWebAppSite.EnsureConfig(_temp.Path));

        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(_temp.Path, StaticWebAppSite.ConfigFileName)))!;
        Assert.Equal("/index.html", config["navigationFallback"]!["rewrite"]!.GetValue<string>());
        Assert.Contains("/_framework/*", config["navigationFallback"]!["exclude"]!.AsArray().Select(e => e!.GetValue<string>()));
    }

    [Fact]
    public void EnsureConfig_KeepsTheAppsOwnConfig()
    {
        var path = _temp.CreateFile(StaticWebAppSite.ConfigFileName, "{\"routes\":[]}");

        Assert.False(StaticWebAppSite.EnsureConfig(_temp.Path));
        Assert.Equal("{\"routes\":[]}", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("production", null)]
    [InlineData("staging", "staging")]
    public void ClientEnvironment_MatchesSwaDeploy(string? environment, string? expectedEnvironment)
    {
        var variables = StaticWebAppSite.CreateClientEnvironment("/site", "token", environment);

        Assert.Equal("upload", variables["DEPLOYMENT_ACTION"]);
        Assert.Equal("true", variables["SKIP_APP_BUILD"]);
        Assert.Equal("/site", variables["APP_LOCATION"]);
        Assert.Equal("token", variables["DEPLOYMENT_TOKEN"]);
        Assert.Equal(expectedEnvironment, variables.GetValueOrDefault("DEPLOYMENT_ENVIRONMENT"));
    }

    [Fact]
    public void Target_UsesAzOrADeploymentToken()
    {
        var target = CreateTarget();
        var viaAz = Profile();
        var viaToken = Profile();
        viaToken[ProfileProperties.PubwizAzureAuthentication] = StaticWebAppTarget.DeploymentTokenAuthentication;

        Assert.True(target.CanPublish(viaAz));
        Assert.False(target.RequiresPassword(viaAz));
        Assert.True(target.RequiresPassword(viaToken));
    }

    [Fact]
    public void Target_BuildsPreparesAndDeploys()
    {
        var project = new ProjectInfo { Name = "Wasm", FilePath = "/src/Wasm/Wasm.csproj", Kind = ProjectKind.BlazorWasm };

        Assert.Equal(["Build and publish", "Prepare site", "Deploy to Static Web Apps", "Done"],
            CreateTarget().CreateSteps(new PublishRequest(project, Profile())).Select(s => s.Name));
    }

    private static StaticWebAppTarget CreateTarget()
    {
        var runner = new ProcessRunner();
        return new StaticWebAppTarget(new DotnetCli(runner, "dotnet"), new AzCli(runner), new StaticSitesClientDownloader(new HttpClient()), runner);
    }

    private static PublishProfile Profile()
    {
        var profile = new PublishProfile("Swa");
        profile[ProfileProperties.WebPublishMethod] = "Custom";
        profile[ProfileProperties.PublishProvider] = ProfileProperties.AzureStaticWebAppProvider;
        profile[ProfileProperties.PubwizAzureResourceGroup] = "rg";
        profile[ProfileProperties.PubwizAzureAppName] = "wasm";
        return profile;
    }

    public void Dispose() => _temp.Dispose();
}
