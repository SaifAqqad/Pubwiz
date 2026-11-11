using Pubwiz.Core.Discovery;
using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Infrastructure.Platform;
using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Presets;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Publishing.Steps;
using Pubwiz.Core.Targets.Azure;
using Pubwiz.Core.Targets.Azure.Kudu;
using Pubwiz.Core.Targets.Folder;
using Pubwiz.Core.Targets.ServerLogins;
using Pubwiz.Core.Targets.WebDeploy;
using Pubwiz.Core.Tests.TestHelpers;
using Pubwiz.WebDeploy;

namespace Pubwiz.Core.Tests.Publishing;

public sealed class PublishPlatformTests : IDisposable
{
    private static readonly DotnetCli Dotnet = new(new ProcessRunner(), "dotnet");

    /// <summary>An OS that isn't this machine's, so it's built on x64.</summary>
    private static readonly PublishOs OtherOs = PublishPlatform.ThisMachine == PublishOs.Linux ? PublishOs.Windows : PublishOs.Linux;

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static ProjectInfo Project(ProjectKind kind = ProjectKind.Web) =>
        new() { Name = "App", FilePath = Path.Combine(Path.GetTempPath(), "App", "App.csproj"), Kind = kind };

    [Fact]
    public void Resolve_PrefersTheProfilesRuntime()
    {
        var profile = new PublishProfile("Prod") { [ProfileProperties.RuntimeIdentifier] = "linux-arm64" };

        var platform = PublishPlatform.Resolve(Project(), profile, TargetPlatform.Of(PublishOs.Windows), chosenOs: PublishOs.MacOS);

        Assert.Equal(new ResolvedPlatform(PlatformSource.Profile, PublishOs.Linux, "arm64", "linux-arm64"), platform);
        Assert.False(platform.CanChooseArchitecture);
    }

    [Fact]
    public void Resolve_UsesTheTargetsOsOverTheChosenOne()
    {
        var platform = PublishPlatform.Resolve(Project(), new PublishProfile("Prod"), TargetPlatform.Of(PublishOs.Windows), chosenOs: PublishOs.Linux);

        Assert.Equal(new ResolvedPlatform(PlatformSource.Target, PublishOs.Windows, "x64", "win-x64"), platform);
        Assert.False(platform.CanChooseOs);
        Assert.True(platform.CanChooseArchitecture);
    }

    [Fact]
    public void Resolve_UsesTheChosenOsWhenTheTargetCantTell()
    {
        var platform = PublishPlatform.Resolve(Project(), new PublishProfile("Prod"), TargetPlatform.Any, chosenOs: OtherOs);

        Assert.Equal(new ResolvedPlatform(PlatformSource.Chosen, OtherOs, "x64", $"{PublishPlatform.ToIdentifier(OtherOs)}-x64"), platform);
        Assert.True(platform.CanChooseOs);
    }

    [Fact]
    public void Resolve_BuildsForThisMachineWhenNothingDecides()
    {
        var platform = PublishPlatform.Resolve(Project(), new PublishProfile("Prod"), TargetPlatform.Any, chosenOs: null);

        // No -r, like dotnet publish on its own.
        Assert.Equal(new ResolvedPlatform(PlatformSource.ThisMachine, PublishPlatform.ThisMachine, OperatingSystemInfo.Architecture, null), platform);
        Assert.True(platform.CanChooseOs);
    }

    [Fact]
    public void Resolve_IgnoresTheOsWhereItDoesntApply()
    {
        var staticWebApp = PublishPlatform.Resolve(Project(), new PublishProfile("Prod"), TargetPlatform.None, chosenOs: PublishOs.Linux);
        var blazorWasm = PublishPlatform.Resolve(Project(ProjectKind.BlazorWasm), new PublishProfile("Prod"), TargetPlatform.Of(PublishOs.Windows), chosenOs: null);

        Assert.Equal(new ResolvedPlatform(PlatformSource.None, null, null, null), staticWebApp);
        Assert.Equal(new ResolvedPlatform(PlatformSource.None, null, null, null), blazorWasm);
    }

    [Fact]
    public void Resolve_UsesTheChosenArchitectureWithTheTargetsOs()
    {
        // A registry's images can be for Arm servers; the target only assumes x64.
        var platform = PublishPlatform.Resolve(Project(), new PublishProfile("Prod"), TargetPlatform.Of(PublishOs.Linux), chosenOs: PublishOs.Windows, chosenArchitecture: "arm64");

        Assert.Equal(new ResolvedPlatform(PlatformSource.Target, PublishOs.Linux, "arm64", "linux-arm64"), platform);
        Assert.Equal("Linux arm64", platform.DisplayName);
    }

    [Fact]
    public void Resolve_UsesTheChosenArchitectureWithTheChosenOsOrThisMachines()
    {
        var chosen = PublishPlatform.Resolve(Project(), new PublishProfile("Prod"), TargetPlatform.Any, chosenOs: PublishOs.Linux, chosenArchitecture: "arm64");
        var thisMachine = PublishPlatform.Resolve(Project(), new PublishProfile("Prod"), TargetPlatform.Any, chosenOs: null, chosenArchitecture: "x86");

        Assert.Equal("linux-arm64", chosen.RuntimeIdentifier);
        Assert.Equal(PlatformSource.ThisMachine, thisMachine.Source);
        Assert.Equal($"{PublishPlatform.ToIdentifier(PublishPlatform.ThisMachine!.Value)}-x86", thisMachine.RuntimeIdentifier);
    }

    [Theory]
    [InlineData("x64", "x64")]
    [InlineData("AMD64", "x64")]
    [InlineData("x86_64", "x64")]
    [InlineData("aarch64", "arm64")]
    [InlineData("arm64", "arm64")]
    [InlineData("x86", "x86")]
    [InlineData("arm", "arm")]
    [InlineData("riscv", null)]
    public void ParseArchitecture_ReadsCommonNames(string name, string? expected) =>
        Assert.Equal(expected, PublishPlatform.ParseArchitecture(name));

    [Theory]
    [InlineData("linux", true, PublishOs.Linux, null)]
    [InlineData("linux-arm64", true, PublishOs.Linux, "arm64")]
    [InlineData("Windows-AMD64", true, PublishOs.Windows, "x64")]
    [InlineData("osx-x64", true, PublishOs.MacOS, "x64")]
    [InlineData("linux-riscv", false, default(PublishOs), null)]
    [InlineData("arm64", false, default(PublishOs), null)]
    public void TryParsePlatform_ReadsAnOsWithOrWithoutItsArchitecture(string value, bool valid, PublishOs os, string? architecture)
    {
        Assert.Equal(valid, PublishPlatform.TryParsePlatform(value, out var parsedOs, out var parsedArchitecture));
        if (valid)
        {
            Assert.Equal(os, parsedOs);
            Assert.Equal(architecture, parsedArchitecture);
        }
    }

    [Theory]
    [InlineData("win", PublishOs.Windows)]
    [InlineData("Windows", PublishOs.Windows)]
    [InlineData("linux", PublishOs.Linux)]
    [InlineData("osx", PublishOs.MacOS)]
    [InlineData("macOS", PublishOs.MacOS)]
    [InlineData("freebsd", null)]
    [InlineData("", null)]
    public void ParseOs_ReadsRuntimeAndCommonNames(string name, PublishOs? expected) =>
        Assert.Equal(expected, PublishPlatform.ParseOs(name));

    [Theory]
    [InlineData(ProfileProperties.IsLinux, "true", PublishOs.Linux)]
    [InlineData(ProfileProperties.IsLinux, "False", PublishOs.Windows)]
    [InlineData(ProfileProperties.PubwizAzureLinux, "true", PublishOs.Linux)]
    [InlineData(null, null, null)]
    public void AzureProfile_TakesTheOsFromTheApp(string? property, string? value, PublishOs? expected)
    {
        var profile = new PublishProfile("Azure");
        if (property is not null)
        {
            profile[property] = value;
        }

        var platform = AzureProfile.GetPlatform(profile);

        Assert.True(platform.Applies);
        Assert.Equal(expected, platform.Os);
    }

    [Fact]
    public void Resolve_BuildsForAnyCpuWhenTheServerRunsEither()
    {
        var platform = PublishPlatform.Resolve(Project(), new PublishProfile("Prod"), TargetPlatform.OfAnyArchitecture(PublishOs.Windows), chosenOs: PublishOs.Linux);

        // No -r: the app runs on a 32-bit or a 64-bit server.
        Assert.Equal(new ResolvedPlatform(PlatformSource.Target, PublishOs.Windows, null, null), platform);
        Assert.Equal("Windows any CPU", platform.DisplayName);
        Assert.True(platform.CanChooseArchitecture);
    }

    [Fact]
    public void Resolve_UsesTheChosenArchitectureWhenTheServerRunsEither()
    {
        var platform = PublishPlatform.Resolve(Project(), new PublishProfile("Prod"), TargetPlatform.OfAnyArchitecture(PublishOs.Windows), chosenOs: null, chosenArchitecture: "x86");

        Assert.Equal("win-x86", platform.RuntimeIdentifier);
    }

    [Fact]
    public void Resolve_BuildsSelfContainedAppsForX64WhenTheServerRunsEither()
    {
        // A self-contained app needs a runtime identifier, so there's no any-CPU build of it.
        var profile = new PublishProfile("Prod") { [ProfileProperties.SelfContained] = "true" };

        var platform = PublishPlatform.Resolve(Project(), profile, TargetPlatform.OfAnyArchitecture(PublishOs.Windows), chosenOs: null);

        Assert.Equal("win-x64", platform.RuntimeIdentifier);
    }

    [Theory]
    [InlineData("false", null)]
    [InlineData("true", "linux-x64")]
    public void WebJobJob_BuildsForAnyCpuOnWindowsAndX64OnLinux(string isLinux, string? expectedRuntime)
    {
        // Windows apps run 32-bit unless their "64 Bit" setting is on, and their WebJobs run the same way.
        var profile = new PublishProfile("WebJob")
        {
            [ProfileProperties.WebPublishMethod] = "OneDeploy",
            [ProfileProperties.PublishUrl] = "https://contoso.scm.azurewebsites.net/",
            [ProfileProperties.WebJobName] = "Processor",
            [ProfileProperties.WebJobType] = "Continuous",
            [ProfileProperties.IsLinux] = isLinux
        };

        var target = new AzureWebJobTarget(Dotnet, new KuduClient(new HttpClient()), new KuduCredentialsProvider(new AzCli(new ProcessRunner())));
        var job = new PublishRunner([target]).CreateJob(new PublishRequest(Project(ProjectKind.Worker), profile));

        Assert.Equal(expectedRuntime, BuildOptions(job).RuntimeIdentifier);
    }

    [Fact]
    public void WebDeployJob_BuildsForWindowsWhateverWasChosen()
    {
        var profile = new PublishProfile("Iis")
        {
            [ProfileProperties.WebPublishMethod] = "MSDeploy",
            [ProfileProperties.MSDeployServiceURL] = "iis.contoso.local",
            [ProfileProperties.DeployIisAppPath] = "Default Web Site/api",
            [ProfileProperties.UserName] = "deploy"
        };

        var servers = new ServerCredentialStore(new MemorySecretStore(), Path.Combine(_temp.Path, "servers.json"));
        var target = new WebDeployTarget(Dotnet, new MsDeployCli(new ProcessRunner(), null), new WebDeployClient(), servers);
        var job = new PublishRunner([target]).CreateJob(new PublishRequest(Project(), profile) { Os = PublishOs.Linux, Password = "s3cret" });

        Assert.Equal("win-x64", BuildOptions(job).RuntimeIdentifier);
    }

    [Fact]
    public void Preset_RejectsAnUnknownArchitecture()
    {
        var path = Path.Combine(_temp.Path, PresetFile.FileName);
        File.WriteAllText(path, """{ "presets": { "prod": { "arch": "riscv", "projects": [] } } }""");

        var error = Assert.Throws<InvalidDataException>(() => PresetFile.Load(path));
        Assert.Contains("\"arch\"", error.Message);
    }

    [Fact]
    public void FolderJob_BuildsForTheChosenOsOrThisMachine()
    {
        var profile = new PublishProfile("Folder") { [ProfileProperties.WebPublishMethod] = "FileSystem", [ProfileProperties.PublishUrl] = "out/" };
        var runner = new PublishRunner([new FolderTarget(Dotnet)]);

        var chosen = runner.CreateJob(new PublishRequest(Project(), profile) { Os = OtherOs });
        var thisMachine = runner.CreateJob(new PublishRequest(Project(), profile));

        Assert.Equal($"{PublishPlatform.ToIdentifier(OtherOs)}-x64", BuildOptions(chosen).RuntimeIdentifier);
        Assert.Null(BuildOptions(thisMachine).RuntimeIdentifier);
    }

    [Fact]
    public void Preset_RejectsAnUnknownOs()
    {
        var path = Path.Combine(_temp.Path, PresetFile.FileName);
        File.WriteAllText(path, """{ "presets": { "prod": { "os": "beos", "projects": [] } } }""");

        var error = Assert.Throws<InvalidDataException>(() => PresetFile.Load(path));
        Assert.Contains("\"os\"", error.Message);
    }

    private static DotnetPublishOptions BuildOptions(PublishJob job) => job.Steps.OfType<DotnetPublishStep>().Single().Options;
}
