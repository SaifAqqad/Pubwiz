using Pubwiz.Core.Discovery;
using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Infrastructure.Git;
using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Targets;
using Pubwiz.Core.Targets.Aws;
using Pubwiz.Core.Tests.TestHelpers;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Pubwiz.Core.Tests.Targets.Aws;

public sealed class AwsTargetTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    private static readonly ProjectInfo Project = new() { Name = "Shop", FilePath = "/src/Shop/Shop.csproj", Kind = ProjectKind.Web, TargetFrameworks = ["net10.0"] };

    // Elastic Beanstalk

    [Fact]
    public void Bundle_ForWindows_AddsTheDeploymentManifest()
    {
        var zipPath = CreateBundle(windows: true, ("Shop.dll", ""), ("Shop.runtimeconfig.json", FrameworkDependentConfig));

        using var zip = ZipFile.OpenRead(zipPath);
        using var reader = new StreamReader(zip.GetEntry(BeanstalkBundle.WindowsManifestName)!.Open());
        var manifest = JsonNode.Parse(reader.ReadToEnd())!;
        var parameters = manifest["deployments"]!["aspNetCoreWeb"]![0]!["parameters"]!;

        Assert.Equal(".", parameters["appBundle"]!.GetValue<string>());
        Assert.Equal("/", parameters["iisPath"]!.GetValue<string>());
        Assert.Equal("Default Web Site", parameters["iisWebSite"]!.GetValue<string>());
        Assert.NotNull(zip.GetEntry("Shop.dll"));
    }

    [Fact]
    public void Bundle_ForWindows_KeepsTheAppsOwnManifest()
    {
        var zipPath = CreateBundle(windows: true, ("Shop.dll", ""), (BeanstalkBundle.WindowsManifestName, "{\"custom\":true}"));

        using var zip = ZipFile.OpenRead(zipPath);
        using var reader = new StreamReader(zip.GetEntry(BeanstalkBundle.WindowsManifestName)!.Open());
        Assert.Equal("{\"custom\":true}", reader.ReadToEnd());
    }

    [Fact]
    public void Bundle_ForLinux_StartsSelfContainedAppsWithAProcfile()
    {
        var zipPath = CreateBundle(windows: false, ("Shop", "ELF"), ("Shop.runtimeconfig.json", SelfContainedConfig));

        using var zip = ZipFile.OpenRead(zipPath);
        using var reader = new StreamReader(zip.GetEntry(BeanstalkBundle.ProcfileName)!.Open());
        Assert.Equal("web: ./Shop", reader.ReadToEnd());
        Assert.Equal(0b111_101_101, (zip.GetEntry("Shop")!.ExternalAttributes >> 16) & 0b111_111_111);
    }

    [Fact]
    public void Bundle_ForLinux_LeavesFrameworkDependentAppsAlone()
    {
        var zipPath = CreateBundle(windows: false, ("Shop.dll", ""), ("Shop.runtimeconfig.json", FrameworkDependentConfig));

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Null(zip.GetEntry(BeanstalkBundle.ProcfileName));
        Assert.Null(zip.GetEntry(BeanstalkBundle.WindowsManifestName));
    }

    [Fact]
    public void VersionLabel_HasTheTimeAndCommit()
    {
        var git = new GitInfo("/src", "main", "2a2755a0000", "2a2755a", "Fix checkout", "Saif", DateTimeOffset.UnixEpoch, HasChanges: false);
        var request = new PublishRequest(Project, new PublishProfile("Prod")) { Git = git };

        Assert.Equal("Shop-20261102-153000-2a2755a", ElasticBeanstalkTarget.CreateVersionLabel(request, new DateTimeOffset(2026, 11, 2, 15, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void ElasticBeanstalk_PublishesUploadsAndDeploys()
    {
        var target = new ElasticBeanstalkTarget(new DotnetCli(new ProcessRunner(), "dotnet"), new AwsCli(new ProcessRunner()));
        var profile = AwsProfileFor(ProfileProperties.AwsElasticBeanstalkProvider,
            (ProfileProperties.PubwizAwsApplication, "shop"), (ProfileProperties.PubwizAwsEnvironment, "shop-prod"));

        Assert.True(target.CanPublish(profile));
        Assert.False(target.RequiresPassword(profile));
        Assert.Equal(["Build and publish", "Package", "Upload version", "Deploy to Elastic Beanstalk", "Done"],
            target.CreateSteps(new PublishRequest(Project, profile)).Select(s => s.Name));
    }

    [Fact]
    public void SetupError_NamesMissingSettings()
    {
        var aws = new AwsCli(new ProcessRunner());
        var profile = AwsProfileFor(ProfileProperties.AwsLambdaProvider);
        profile[ProfileProperties.PubwizAwsRegion] = null;

        var error = AwsProfile.GetSetupError(profile, aws, ProfileProperties.PubwizAwsFunction);

        Assert.NotNull(error);
        Assert.Contains(aws.IsInstalled ? "PubwizAwsRegion or PubwizAwsFunction" : "isn't installed", error);
    }

    // Lambda

    [Fact]
    public async Task LambdaPackage_MakesBootstrapExecutable()
    {
        var folder = CreatePublishFolder(("bootstrap", "ELF"), ("bootstrap.dll", ""));
        var zipPath = Path.Combine(_temp.Path, "lambda.zip");

        await RunPackageStepAsync(new LambdaTarget.PackageStep(zipPath, customRuntime: true), folder);

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Equal(0b111_101_101, (zip.GetEntry("bootstrap")!.ExternalAttributes >> 16) & 0b111_111_111);
    }

    [Fact]
    public async Task LambdaPackage_ForCustomRuntimes_NeedsBootstrap()
    {
        var folder = CreatePublishFolder(("Shop.dll", ""));

        var error = await Assert.ThrowsAsync<PublishStepException>(() =>
            RunPackageStepAsync(new LambdaTarget.PackageStep(Path.Combine(_temp.Path, "lambda.zip"), customRuntime: true), folder));

        Assert.Contains("<AssemblyName>bootstrap</AssemblyName>", error.Message);
    }

    [Fact]
    public void LambdaFunction_PicksTheRuntimeIdentifier()
    {
        var functions = AwsCli.Deserialize<List<LambdaFunction>>("""
            [{"name":"orders","runtime":"dotnet8","architectures":["arm64"],"packageType":"Zip"},
             {"name":"aot","runtime":"provided.al2023","architectures":["x86_64"],"packageType":"Zip"}]
            """)!;

        Assert.Equal("linux-arm64", functions[0].RuntimeIdentifier);
        Assert.False(functions[0].IsCustomRuntime);
        Assert.Equal("linux-x64", functions[1].RuntimeIdentifier);
        Assert.True(functions[1].IsCustomRuntime);
    }

    // ECS

    [Fact]
    public void TaskDefinition_ReplacesTheImageAndDropsReadOnlyFields()
    {
        var (json, container) = EcsTaskDefinition.WithImage(DescribedTaskDefinition("web"), "123.dkr.ecr.eu-west-1.amazonaws.com/shop:v2", containerName: null);

        var definition = JsonNode.Parse(json)!;
        Assert.Equal("web", container);
        Assert.Equal("123.dkr.ecr.eu-west-1.amazonaws.com/shop:v2", definition["containerDefinitions"]![0]!["image"]!.GetValue<string>());
        Assert.Equal("shop", definition["family"]!.GetValue<string>());
        Assert.Null(definition["taskDefinitionArn"]);
        Assert.Null(definition["revision"]);
        Assert.Null(definition["registeredAt"]);
    }

    [Fact]
    public void TaskDefinition_WithSeveralContainers_PicksTheOneRunningTheSameRepository()
    {
        var (_, container) = EcsTaskDefinition.WithImage(DescribedTaskDefinition("web", "sidecar"), "123.dkr.ecr.eu-west-1.amazonaws.com/shop:v2", containerName: null);

        Assert.Equal("web", container);
    }

    [Fact]
    public void TaskDefinition_UsesTheNamedContainer()
    {
        var (json, container) = EcsTaskDefinition.WithImage(DescribedTaskDefinition("web", "sidecar"), "registry/other:1", containerName: "sidecar");

        Assert.Equal("sidecar", container);
        Assert.Equal("registry/other:1", JsonNode.Parse(json)!["containerDefinitions"]![1]!["image"]!.GetValue<string>());
    }

    [Fact]
    public void TaskDefinition_AsksForANameWhenItCantTell()
    {
        var error = Assert.Throws<ArgumentException>(() => EcsTaskDefinition.WithImage(DescribedTaskDefinition("web", "sidecar"), "registry/unknown:1", containerName: null));

        Assert.Contains(ProfileProperties.PubwizAwsContainerName, error.Message);
    }

    [Theory]
    [InlineData("registry:5000/shop:1.0", "registry:5000/shop")]
    [InlineData("123.dkr.ecr.eu-west-1.amazonaws.com/team/shop", "123.dkr.ecr.eu-west-1.amazonaws.com/team/shop")]
    [InlineData("shop@sha256:abc", "shop")]
    public void RepositoryOf_StripsTagAndDigest(string image, string expected)
    {
        Assert.Equal(expected, EcsTaskDefinition.RepositoryOf(image));
    }

    [Fact]
    public void ResourceName_IsTheEndOfTheArn()
    {
        Assert.Equal("prod", AwsCli.ResourceName("arn:aws:ecs:eu-west-1:123456789012:cluster/prod"));
    }

    private const string FrameworkDependentConfig = """{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}""";

    private const string SelfContainedConfig = """{"runtimeOptions":{"tfm":"net10.0","includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"}]}}""";

    private static string DescribedTaskDefinition(params string[] containers)
    {
        var definitions = new JsonArray(containers.Select(name => (JsonNode)new JsonObject
        {
            ["name"] = name,
            ["image"] = name == "web" ? "123.dkr.ecr.eu-west-1.amazonaws.com/shop:v1" : "public.ecr.aws/datadog/agent:latest",
            ["essential"] = true
        }).ToArray());

        return new JsonObject
        {
            ["taskDefinitionArn"] = "arn:aws:ecs:eu-west-1:123:task-definition/shop:7",
            ["family"] = "shop",
            ["revision"] = 7,
            ["status"] = "ACTIVE",
            ["containerDefinitions"] = definitions,
            ["requiresAttributes"] = new JsonArray(),
            ["compatibilities"] = new JsonArray("EC2", "FARGATE"),
            ["registeredAt"] = "2026-11-01T10:00:00Z",
            ["registeredBy"] = "arn:aws:iam::123:user/saif",
            ["cpu"] = "256",
            ["memory"] = "512"
        }.ToJsonString();
    }

    private static PublishProfile AwsProfileFor(string provider, params (string Name, string Value)[] properties)
    {
        var profile = new PublishProfile("Aws");
        profile[ProfileProperties.WebPublishMethod] = "Custom";
        profile[ProfileProperties.PublishProvider] = provider;
        profile[ProfileProperties.PubwizAwsRegion] = "eu-west-1";
        foreach (var (name, value) in properties)
        {
            profile[name] = value;
        }

        return profile;
    }

    private string CreatePublishFolder(params (string Name, string Content)[] files)
    {
        var folder = Path.Combine(_temp.Path, $"publish-{Guid.NewGuid():N}");
        foreach (var (name, content) in files)
        {
            _temp.CreateFile(Path.Combine(Path.GetFileName(folder), name), content);
        }

        return folder;
    }

    private string CreateBundle(bool windows, params (string Name, string Content)[] files)
    {
        var zipPath = Path.Combine(_temp.Path, $"bundle-{Guid.NewGuid():N}.zip");
        BeanstalkBundle.Create(CreatePublishFolder(files), zipPath, windows);
        return zipPath;
    }

    private static async Task RunPackageStepAsync(PublishStep step, string publishFolder)
    {
        var job = new PublishJob(new PublishRequest(Project, new PublishProfile("Lambda")), PublishTargetType.AwsLambda, [step], setupError: null);
        var context = new PublishContext(job) { OutputDirectory = publishFolder };
        await step.ExecuteAsync(context, TestContext.Current.CancellationToken);
    }

    public void Dispose() => _temp.Dispose();
}
