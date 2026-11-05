using Pubwiz.Core.Discovery;
using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Targets.Folder;
using Pubwiz.Core.Tests.TestHelpers;

namespace Pubwiz.Core.Tests.Targets.Folder;

/// <summary>
/// Publishes a real (tiny) console app with <c>dotnet publish</c>.
/// </summary>
public sealed class FolderTargetTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FolderTarget _target = new(new DotnetCli(new ProcessRunner(), DotnetLocator.Find()!));

    [Fact]
    public async Task Publish_WritesTheAppToTheProfileFolder()
    {
        var job = await PublishAsync("""System.Console.WriteLine("hi");""");

        Assert.Null(job.Error);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(Path.Combine(_temp.Path, "App", "out"), job.OutputDirectory);
        Assert.True(File.Exists(Path.Combine(job.OutputDirectory!, "App.dll")));
    }

    [Fact]
    public async Task Publish_ImportsProfilesWithDotsInTheirName()
    {
        // The SDK used to read "App.Api - contoso.azurecr.io" as a file name with the extension ".io" and skip the
        // profile (NETSDK1198). AssemblyName only comes from the profile here, so the dll name shows it was imported.
        var job = await PublishAsync("""System.Console.WriteLine("hi");""", "App.Api - contoso.azurecr.io", ("AssemblyName", nameof(Publish_ImportsProfilesWithDotsInTheirName)));

        Assert.Null(job.Error);
        Assert.True(File.Exists(Path.Combine(job.OutputDirectory!, $"{nameof(Publish_ImportsProfilesWithDotsInTheirName)}.dll")));
    }

    [Fact]
    public async Task Publish_ReportsCompilerErrors()
    {
        var job = await PublishAsync("this is not C#");

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("Build and publish", job.FailedStep?.Name);
        Assert.Contains("error CS", job.Error);
    }

    private async Task<PublishJob> PublishAsync(string code, string profileName = "Folder", params (string Name, string Value)[] properties)
    {
        var projectPath = _temp.CreateProject("App/App.csproj", properties: "<TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>");
        _temp.CreateFile("App/Program.cs", code);

        var profile = new PublishProfile(profileName);
        profile[ProfileProperties.WebPublishMethod] = "FileSystem";
        profile[ProfileProperties.PublishUrl] = "out/";
        foreach (var (name, value) in properties)
        {
            profile[name] = value;
        }

        var savedProfile = PublishProfileStore.Save(Path.GetDirectoryName(projectPath)!, profile);

        var project = new ProjectInfo { Name = "App", FilePath = projectPath, Kind = ProjectKind.Console, TargetFrameworks = ["net10.0"] };
        var runner = new PublishRunner([_target]);
        var job = runner.CreateJob(new PublishRequest(project, savedProfile));

        await runner.RunAsync([job], 1, TestContext.Current.CancellationToken);
        return job;
    }

    public void Dispose() => _temp.Dispose();
}
