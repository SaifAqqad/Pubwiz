using Pubwiz.Core.Discovery;
using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Targets;
using Pubwiz.Core.Targets.Aws;
using Pubwiz.Core.Targets.Azure;
using Pubwiz.Core.Targets.Containers;
using Pubwiz.Core.Tests.TestHelpers;

namespace Pubwiz.Core.Tests.Targets.Containers;

/// <summary>
/// Builds real images with the .NET SDK and podman. Skipped when podman isn't running.
/// The first run downloads the .NET base image from mcr.microsoft.com.
/// </summary>
public sealed class ContainerPublishTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly ProcessRunner _processRunner = new();
    private readonly string _repository = $"pubwiz-test-{Guid.NewGuid():N}"[..24];

    private PublishRunner CreateRunner() => new(ContainerTarget.Destinations.Select(destination => (IPublishTarget)new ContainerTarget(
        new DotnetCli(_processRunner, DotnetLocator.Find()!), new ContainerCli(_processRunner), new AzCli(_processRunner), new AwsCli(_processRunner), destination)));

    [Fact]
    public async Task SdkBuild_WritesATarball()
    {
        var job = await PublishAsync((ProfileProperties.ContainerArchiveOutputPath, "image/"));

        Assert.Null(job.Error);
        Assert.Equal(PublishTargetType.ContainerTarball, job.TargetType);
        Assert.True(new FileInfo(job.Destination!).Length > 1_000_000);
    }

    [Fact]
    public async Task SdkBuild_LoadsTheImageIntoPodmanThenADockerfileBuildsOnTopOfIt()
    {
        await SkipWithoutPodmanAsync();

        try
        {
            var sdkJob = await PublishAsync((ProfileProperties.LocalRegistry, "Podman"));
            Assert.Null(sdkJob.Error);
            Assert.True(await ImageExistsAsync($"{_repository}:latest"));

            // Built on the SDK image, so no extra base image is downloaded.
            _temp.CreateFile("App/Dockerfile", $"FROM localhost/{_repository}:latest\nLABEL built-by=pubwiz-test\n");
            var dockerfileJob = await PublishAsync(
                (ProfileProperties.PubwizContainerBuilder, "Dockerfile"),
                (ProfileProperties.PubwizDockerContext, "."),
                (ProfileProperties.LocalRegistry, "Podman"),
                (ProfileProperties.ContainerImageTags, "dockerfile"));

            Assert.Null(dockerfileJob.Error);
            Assert.True(await ImageExistsAsync($"{_repository}:dockerfile"));
        }
        finally
        {
            await _processRunner.RunAsync(new ProcessSpec("podman", ["rmi", "-f", $"{_repository}:latest", $"{_repository}:dockerfile"]), cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    private async Task<PublishJob> PublishAsync(params (string Name, string Value)[] properties)
    {
        var projectPath = Path.Combine(_temp.Path, "App", "App.csproj");
        if (!File.Exists(projectPath))
        {
            _temp.CreateProject("App/App.csproj", properties: "<TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>");
            _temp.CreateFile("App/Program.cs", """System.Console.WriteLine("hi");""");
        }

        var profile = new PublishProfile($"Container{Guid.NewGuid():N}");
        profile[ProfileProperties.WebPublishMethod] = "Container";
        profile[ProfileProperties.ContainerRepository] = _repository;
        foreach (var (name, value) in properties)
        {
            profile[name] = value;
        }

        var saved = PublishProfileStore.Save(Path.GetDirectoryName(projectPath)!, profile);
        var project = new ProjectInfo { Name = "App", FilePath = projectPath, Kind = ProjectKind.Console, TargetFrameworks = ["net10.0"] };
        var runner = CreateRunner();
        var job = runner.CreateJob(new PublishRequest(project, saved));
        await runner.RunAsync([job], 1, TestContext.Current.CancellationToken);
        return job;
    }

    private async Task SkipWithoutPodmanAsync()
    {
        if (!(await new ContainerCli(_processRunner).DetectAsync(TestContext.Current.CancellationToken)).Contains(ContainerTool.Podman))
        {
            Assert.Skip("podman isn't running here.");
        }
    }

    private async Task<bool> ImageExistsAsync(string image) =>
        (await _processRunner.RunAsync(new ProcessSpec("podman", ["image", "exists", image]), cancellationToken: TestContext.Current.CancellationToken)).Succeeded;

    public void Dispose() => _temp.Dispose();
}
