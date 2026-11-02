using Pubwiz.Core.Discovery;
using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Targets;
using Pubwiz.Core.Targets.Azure;
using Pubwiz.Core.Targets.Azure.Kudu;
using Pubwiz.Core.Targets.WebDeploy;
using Pubwiz.Core.Tests.TestHelpers;
using Pubwiz.WebDeploy;

namespace Pubwiz.Core.Tests.Targets.WebDeploy;

public sealed class WebDeployTargetTests : IDisposable
{
    private static readonly DotnetCli Dotnet = new(new ProcessRunner(), "dotnet");

    private readonly TempDirectory _temp = new();
    private readonly ServerCredentialStore _servers;

    public WebDeployTargetTests() => _servers = new ServerCredentialStore(new MemorySecretStore(), Path.Combine(_temp.Path, "servers.json"));

    public void Dispose() => _temp.Dispose();

    private WebDeployTarget Target(bool installed) =>
        new(Dotnet, new MsDeployCli(new ProcessRunner(), installed ? @"C:\msdeploy.exe" : null), new WebDeployClient(), _servers);

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
    public void CreateJob_UsesTheBuiltInClientWithoutMsDeploy()
    {
        // A Basic-auth profile (it has a user name) publishes with the built-in client even where msdeploy.exe is missing.
        var job = new PublishRunner([Target(installed: false)]).CreateJob(Request(Profile()));

        Assert.Equal(PublishTargetType.WebDeploy, job.TargetType);
        Assert.Null(job.Error);
        Assert.Equal(["Build and publish", "Deploy (Web Deploy)", "Done"], job.Steps.Select(s => s.Name));
    }

    [Fact]
    public void CreateJob_FailsWithoutMsDeployWhenTheProfileSignsInAsTheCurrentWindowsUser()
    {
        var windowsAccount = Profile();
        windowsAccount[ProfileProperties.UserName] = null;

        var job = new PublishRunner([Target(installed: false)]).CreateJob(Request(windowsAccount));

        Assert.Empty(job.Steps);
        Assert.Equal(WebDeployProfile.BuiltInClientBlocker(windowsAccount), job.Error);
    }

    [Fact]
    public void CreateSyncOptions_ReadsChecksumAndTheVisualStudioSkipRules()
    {
        var profile = Profile();
        profile[ProfileProperties.MSDeployUseChecksum] = "True";
        profile.Items.Add(SkipRule("KeepUploads", "dirPath", @"wwwroot\\uploads$", skipAction: "Delete"));
        profile.Items.Add(SkipRule("SkipLogs", "filePath", @"\.log$"));
        profile.Items.Add(new ProfileItem("Content", "readme.txt", new Dictionary<string, string>()));

        var options = WebDeployTarget.CreateSyncOptions(Request(profile) with { WhatIf = true }, WebDeployProfile.GetEndpoint(profile), "/staging");

        Assert.True(options.UseChecksum);
        Assert.True(options.WhatIf);
        Assert.Equal(
        [
            new WebDeploySkipRule(WebDeploySkipObject.Directory, @"wwwroot\\uploads$") { Action = WebDeploySkipAction.Delete },
            new WebDeploySkipRule(WebDeploySkipObject.File, @"\.log$")
        ], options.SkipRules);
    }

    [Theory]
    [InlineData("metaKey", "x", null, "ObjectName")]
    [InlineData("dirPath", null, null, "AbsolutePath")]
    [InlineData("dirPath", "x", "Rename", "SkipAction")]
    [InlineData("dirPath", @"\uploads$", null, "regular expression")]
    public void CreateJob_RefusesASkipRuleItCantApply(string objectName, string? absolutePath, string? skipAction, string expectedInError)
    {
        // Ignoring a "keep these files" rule could let a sync delete them, so the job fails instead.
        var profile = Profile();
        profile.Items.Add(SkipRule("Mine", objectName, absolutePath, skipAction));

        var job = new PublishRunner([Target(installed: false)]).CreateJob(Request(profile));

        Assert.Empty(job.Steps);
        Assert.Contains("'Mine'", job.Error);
        Assert.Contains(expectedInError, job.Error);
    }

    [Fact]
    public void CreateJob_PreviewsWithoutTheDestinationStep()
    {
        var job = new PublishRunner([Target(installed: false)]).CreateJob(Request(Profile()) with { WhatIf = true });

        Assert.Equal(["Build and publish", "Preview changes (Web Deploy)"], job.Steps.Select(s => s.Name));
    }

    private static ProfileItem SkipRule(string name, string objectName, string? absolutePath, string? skipAction = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ObjectName"] = objectName };
        if (absolutePath is not null)
        {
            metadata["AbsolutePath"] = absolutePath;
        }

        if (skipAction is not null)
        {
            metadata["SkipAction"] = skipAction;
        }

        return new ProfileItem(ProfileProperties.MsDeploySkipRules, name, metadata);
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

    [Theory]
    [InlineData(null, null, true)] // the default, whatever the profile says
    [InlineData("false", null, true)]
    [InlineData("true", false, false)] // --no-app-offline
    public void CreateSyncOptions_TakesTheAppOfflineUnlessTurnedOffForTheRun(string? profileValue, bool? requestValue, bool expected)
    {
        var profile = Profile();
        profile[ProfileProperties.EnableMSDeployAppOffline] = profileValue;
        var request = Request(profile) with { TakeAppOffline = requestValue };

        var options = WebDeployTarget.CreateSyncOptions(request, WebDeployProfile.GetEndpoint(profile), "/staging");

        Assert.Equal(expected, options.TakeAppOffline);
    }

    [Theory]
    [InlineData("false", true, true)] // trusted for this run
    [InlineData("true", false, false)] // the user said no
    [InlineData("true", null, true)] // not asked: the profile decides
    public void CreateSyncOptions_TheRequestOverridesTheProfilesCertificateSetting(string profileValue, bool? requestValue, bool expected)
    {
        var profile = Profile();
        profile[ProfileProperties.AllowUntrustedCertificate] = profileValue;
        var request = Request(profile) with { AllowUntrustedCertificate = requestValue };

        var options = WebDeployTarget.CreateSyncOptions(request, WebDeployProfile.GetEndpoint(profile), "/staging");

        Assert.Equal(expected, options.AllowUntrustedCertificate);
    }

    [Fact]
    public void Catalog_OffersWebDeployEverywhere_WithoutAnOsWarning()
    {
        // The built-in client publishes from any OS, so Web Deploy is offered without a "needs Windows" caveat.
        var webDeploy = PublishTargetCatalog.For(ProjectKind.Web).Single(t => t.Type == PublishTargetType.WebDeploy);

        Assert.True(webDeploy.IsAvailable);
        Assert.Null(webDeploy.Warning);
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
    public void Alternatives_AreOnlyNeededWhenNoEngineCanPublishTheProfile()
    {
        var folder = new PublishProfile("Folder");
        folder[ProfileProperties.WebPublishMethod] = "FileSystem";

        var windowsAccount = Profile();
        windowsAccount[ProfileProperties.UserName] = null;

        // A Basic-auth profile is handled by the built-in client, so it never needs alternatives.
        Assert.False(WebDeployAlternatives.AreNeeded(Profile(), _servers));
        // A current-Windows-user profile needs msdeploy.exe; without it, alternatives are offered.
        Assert.Equal(MsDeployLocator.Find() is null, WebDeployAlternatives.AreNeeded(windowsAccount, _servers));
        Assert.False(WebDeployAlternatives.AreNeeded(Profile("contoso.scm.azurewebsites.net:443"), _servers));
        Assert.False(WebDeployAlternatives.AreNeeded(folder, _servers));
    }

    [Fact]
    public void TryGetEndpoint_IsNullForAnIncompleteProfile()
    {
        Assert.Null(WebDeployAlternatives.TryGetEndpoint(Profile(site: null)));
        Assert.Equal("iis.contoso.local", WebDeployAlternatives.TryGetEndpoint(Profile())?.ServiceUrl.Host);
    }

    [Fact]
    public async Task ServerLogin_LetsAWindowsUserProfilePublishWithoutMsDeployOrAPassword()
    {
        var windowsAccount = Profile("https://IIS.contoso.local:8172/msdeploy.axd");
        windowsAccount[ProfileProperties.UserName] = null;
        windowsAccount[ProfileProperties.AuthType] = "NTLM";
        await _servers.SaveAsync("iis.contoso.local", "shared", "p@ss", Token);
        var target = Target(installed: false);

        var job = new PublishRunner([target]).CreateJob(Request(windowsAccount));

        Assert.Null(job.Error);
        Assert.False(target.RequiresPassword(windowsAccount));
        Assert.False(WebDeployAlternatives.AreNeeded(windowsAccount, _servers));
        Assert.Equal(new ServerCredential("iis.contoso.local", "shared"), target.FindServerLogin(windowsAccount));
    }

    [Fact]
    public async Task ServerLogin_ReplacesTheProfilesUserAndPassword()
    {
        await _servers.SaveAsync("iis.contoso.local", "shared", "p@ss", Token);
        var profile = Profile();
        profile[ProfileProperties.AuthType] = "NTLM";
        var context = CreateContext(Request(profile));

        var login = new WebDeployTarget.LoginSource(_servers, _servers.Find("iis.contoso.local"));
        var options = await login.CreateSyncOptionsAsync(context, WebDeployProfile.GetEndpoint(profile), Token);

        Assert.Equal("shared", options.UserName);
        Assert.Equal("p@ss", options.Password);
        Assert.Equal("Basic", options.EffectiveAuthType);
    }

    [Fact]
    public async Task ServerLogin_FailsClearlyWhenItsPasswordIsMissing()
    {
        await _servers.SaveAsync("iis.contoso.local", "shared", "p@ss", Token);
        var secretsWithoutPassword = new ServerCredentialStore(new MemorySecretStore(), _servers.FilePath);
        var context = CreateContext(Request(Profile()));

        var login = new WebDeployTarget.LoginSource(secretsWithoutPassword, secretsWithoutPassword.Find("iis.contoso.local"));
        var error = await Assert.ThrowsAsync<PublishStepException>(() => login.CreateSyncOptionsAsync(context, WebDeployProfile.GetEndpoint(Profile()), Token));

        Assert.Contains("servers set iis.contoso.local", error.Message);
    }

    [Fact]
    public async Task WithoutAServerLogin_TheProfilesUserAndPasswordAreUsed()
    {
        await _servers.SaveAsync("other-server", "shared", "p@ss", Token);
        var target = Target(installed: false);

        Assert.Null(target.FindServerLogin(Profile()));
        Assert.True(target.RequiresPassword(Profile()));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private PublishContext CreateContext(PublishRequest request) =>
        new(new PublishRunner([Target(installed: false)]).CreateJob(request)) { OutputDirectory = "/staging" };
}
