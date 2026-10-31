using Pubwiz.Core.Discovery;
using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Execution;
using Pubwiz.Core.Maui;
using Pubwiz.Core.Processes;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets;
using Pubwiz.Core.Targets.Maui;
using Pubwiz.Core.Tests.TestHelpers;

namespace Pubwiz.Core.Tests.Maui;

public sealed class MauiTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly DotnetCli _dotnet = new(new ProcessRunner(), DotnetLocator.Find()!);

    private static PublishProfile AndroidProfile(bool signed)
    {
        var profile = new PublishProfile("Android");
        profile[ProfileProperties.WebPublishMethod] = "Custom";
        profile[ProfileProperties.PublishProvider] = ProfileProperties.MauiAndroidProvider;
        profile[ProfileProperties.TargetFramework] = "net10.0-android";
        profile[ProfileProperties.AndroidKeyStore] = signed ? "true" : null;
        return profile;
    }

    [Fact]
    public void ParseWorkloadList_ReadsTheIdColumn()
    {
        const string output = """
            Workload version: 10.0.400-manifests.a00bebb6

            Installed Workload Id      Manifest Version      Installation Source
            --------------------------------------------------------------------
            maui-android               10.0.40/10.0.100      SDK 10.0.400
            android                    36.0.0/10.0.100       SDK 10.0.400

            Use `dotnet workload search` to find additional workloads to install.
            """;

        Assert.Equal(["maui-android", "android"], DotnetCli.ParseWorkloadList(output));
        Assert.Empty(DotnetCli.ParseWorkloadList("Installed Workload Id\n------\n\nUse ..."));
    }

    [Fact]
    public void TargetFrameworks_PicksThePlatformFrameworks()
    {
        string[] frameworks = ["net10.0-android", "net10.0-ios", "net10.0-maccatalyst"];

        Assert.Equal(["net10.0-android"], MauiPlatform.Android.TargetFrameworks(frameworks));
        Assert.Equal(["net10.0-ios"], MauiPlatform.Ios.TargetFrameworks(frameworks));
    }

    [Fact]
    public void Android_PassesTheSigningPasswordOnlyThroughAnEnvironmentVariable()
    {
        var target = new MauiTarget(_dotnet, MauiPlatform.Android);
        var project = new ProjectInfo { Name = "App", FilePath = "/src/App/App.csproj", Kind = ProjectKind.Maui };

        var options = target.CreateOptions(new PublishRequest(project, AndroidProfile(signed: true)) { Password = "keystore-secret" }, "/src/App/obj/pubwiz/Android");

        Assert.True(target.RequiresPassword(AndroidProfile(signed: true)));
        Assert.False(target.RequiresPassword(AndroidProfile(signed: false)));
        Assert.Equal("net10.0-android", options.TargetFramework);
        Assert.Equal($"env:{MauiTarget.SigningPasswordVariable}", options.Properties[ProfileProperties.AndroidSigningKeyPass]);
        Assert.Equal("keystore-secret", options.SecretProperties[MauiTarget.SigningPasswordVariable]);
        Assert.DoesNotContain(options.ToArguments(), a => a.Contains("keystore-secret"));
    }

    [Fact]
    public void FindPackages_PrefersSignedAndroidPackages()
    {
        _temp.CreateFile("out/com.contoso.app.aab");
        var signedAab = _temp.CreateFile("out/com.contoso.app-Signed.aab");
        var signedApk = _temp.CreateFile("out/com.contoso.app-Signed.apk");
        _temp.CreateFile("out/com.contoso.app.apk");

        var packages = MauiTarget.CollectPackagesStep.FindPackages(Path.Combine(_temp.Path, "out"), MauiPlatform.Android);

        Assert.Equal([signedAab, signedApk], packages);
    }

    [Fact]
    public void ParseIdentities_ReadsSecurityOutput()
    {
        const string output = """
              1) 0123456789ABCDEF0123456789ABCDEF01234567 "Apple Development: Saif (ABCDE12345)"
              2) 89ABCDEF0123456789ABCDEF0123456789ABCDEF "Apple Distribution: Contoso (TEAM123456)"
                 2 valid identities found
            """;

        Assert.Equal(["Apple Development: Saif (ABCDE12345)", "Apple Distribution: Contoso (TEAM123456)"], AppleSigning.ParseIdentities(output));
    }

    [Fact]
    public void ParseProvisioningProfile_ReadsNameAndExpiry()
    {
        const string plist = """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0">
            <dict>
              <key>AppIDName</key><string>Contoso</string>
              <key>ExpirationDate</key><date>2027-05-01T10:00:00Z</date>
              <key>Name</key><string>Contoso App Store</string>
            </dict>
            </plist>
            """;

        var profile = AppleSigning.ParseProvisioningProfile(plist);

        Assert.Equal("Contoso App Store", profile?.Name);
        Assert.Equal(2027, profile?.ExpiresOn?.Year);
    }

    [Fact]
    public void Catalog_OffersIosOnlyOnMacOS()
    {
        var ios = PublishTargetCatalog.For(ProjectKind.Maui).Single(t => t.Type == PublishTargetType.MauiIos);

        Assert.Equal(OperatingSystem.IsMacOS(), ios.IsAvailable);
    }

    [Fact]
    public async Task AndroidJob_FailsEarlyWithTheInstallCommandWhenTheWorkloadIsMissing()
    {
        var installed = await _dotnet.ListWorkloadsAsync(TestContext.Current.CancellationToken);
        if (MauiPlatform.Android.Workloads().Any(installed.Contains))
        {
            Assert.Skip("The Android workload is installed here.");
        }

        var projectPath = _temp.CreateProject("App/App.csproj", properties: "<TargetFrameworks>net10.0-android</TargetFrameworks><UseMaui>true</UseMaui>");
        var saved = PublishProfileStore.Save(Path.GetDirectoryName(projectPath)!, AndroidProfile(signed: false));
        var project = new ProjectInfo { Name = "App", FilePath = projectPath, Kind = ProjectKind.Maui, TargetFrameworks = ["net10.0-android"] };
        var runner = new PublishRunner([new MauiTarget(_dotnet, MauiPlatform.Android)]);
        var job = runner.CreateJob(new PublishRequest(project, saved));

        await runner.RunAsync([job], 1, TestContext.Current.CancellationToken);

        Assert.Equal("Check workload", job.FailedStep?.Name);
        Assert.Contains("dotnet workload install maui-android", job.Error);
    }

    public void Dispose() => _temp.Dispose();
}
