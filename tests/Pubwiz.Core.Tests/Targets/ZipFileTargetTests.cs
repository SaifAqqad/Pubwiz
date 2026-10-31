using Pubwiz.Core.Discovery;
using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Execution;
using Pubwiz.Core.Processes;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets;
using Pubwiz.Core.Targets.Folder;
using Pubwiz.Core.Tests.TestHelpers;
using System.IO.Compression;

namespace Pubwiz.Core.Tests.Targets;

/// <summary>
/// Publishes a real (tiny) console app to a zip file.
/// </summary>
public sealed class ZipFileTargetTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly DotnetCli _dotnet = new(new ProcessRunner(), DotnetLocator.Find()!);

    private static PublishProfile ZipProfile(string zipFile)
    {
        var profile = new PublishProfile("Zip");
        profile[ProfileProperties.WebPublishMethod] = "FileSystem";
        profile[ProfileProperties.PublishUrl] = "bin/publish/";
        profile[ProfileProperties.PubwizZipFile] = zipFile;
        return profile;
    }

    [Fact]
    public void FindTarget_TellsZipProfilesFromFolderProfiles()
    {
        var runner = new PublishRunner([new ZipFileTarget(_dotnet), new FolderTarget(_dotnet)]);
        var folder = ZipProfile("app.zip");
        folder[ProfileProperties.PubwizZipFile] = null;

        Assert.Equal(PublishTargetType.ZipFile, runner.FindTarget(ZipProfile("app.zip"))?.Type);
        Assert.Equal(PublishTargetType.Folder, runner.FindTarget(folder)?.Type);
        Assert.False(new FolderTarget(_dotnet).CanPublish(ZipProfile("app.zip")));
    }

    [Fact]
    public async Task Publish_ZipsTheAppContents()
    {
        var projectPath = _temp.CreateProject("App/App.csproj", properties: "<TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>");
        _temp.CreateFile("App/Program.cs", """System.Console.WriteLine("hi");""");
        var profile = PublishProfileStore.Save(Path.GetDirectoryName(projectPath)!, ZipProfile(@"out\App.zip"));

        var project = new ProjectInfo { Name = "App", FilePath = projectPath, Kind = ProjectKind.Console, TargetFrameworks = ["net10.0"] };
        var runner = new PublishRunner([new ZipFileTarget(_dotnet)]);
        var job = runner.CreateJob(new PublishRequest(project, profile));
        await runner.RunAsync([job], 1, TestContext.Current.CancellationToken);

        var zipPath = Path.Combine(_temp.Path, "App", "out", "App.zip");
        Assert.Null(job.Error);
        Assert.Equal(zipPath, job.Destination);
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, entry => entry.FullName == "App.dll");
    }

    public void Dispose() => _temp.Dispose();
}
