using Pubwiz.Core.Discovery;
using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Presets;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Tests.TestHelpers;

namespace Pubwiz.Core.Tests.Presets;

public sealed class PresetTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly PresetResolver _resolver = new(new ProjectInspector(new DotnetCli(new ProcessRunner(), DotnetLocator.Find()!)));

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = Path.Combine(_temp.Path, PresetFile.FileName);
        var file = new PresetFile();
        file.Presets["prod"] = new PublishPreset([new PresetProject("src/Api/Api.csproj", "Prod")], "Release", Parallel: 2);

        file.Save(path);
        var loaded = PresetFile.Load(path);

        var preset = loaded.Presets["PROD"];
        Assert.Equal("Release", preset.Configuration);
        Assert.Equal(2, preset.Parallel);
        Assert.Equal([new PresetProject("src/Api/Api.csproj", "Prod")], preset.Projects);
        Assert.Contains("\"presets\"", File.ReadAllText(path));
    }

    [Fact]
    public void SaveThenLoad_KeepsTheOrderAndLeavesItOutWhenMissing()
    {
        var path = Path.Combine(_temp.Path, PresetFile.FileName);
        var file = new PresetFile();
        file.Presets["prod"] = new PublishPreset([new PresetProject("Api.csproj", "Prod", Order: 2), new PresetProject("Web.csproj", "Prod")]);

        file.Save(path);

        Assert.Equal([2, null], PresetFile.Load(path).Presets["prod"].Projects.Select(p => p.Order));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(path), "\"order\""));
    }

    [Fact]
    public void Load_AllowsCommentsAndTrailingCommas()
    {
        var path = _temp.CreateFile(PresetFile.FileName, """
            {
              // shared by the team
              "presets": { "dev": { "projects": [ { "path": "App.csproj", "profile": "Folder" }, ] } },
            }
            """);

        Assert.Single(PresetFile.Load(path).Presets["dev"].Projects);
    }

    [Fact]
    public void Load_ReportsInvalidJson()
    {
        var path = _temp.CreateFile(PresetFile.FileName, "{ nope");

        Assert.Throws<InvalidDataException>(() => PresetFile.Load(path));
    }

    [Fact]
    public void Find_LooksInParentFolders()
    {
        var path = _temp.CreateFile(PresetFile.FileName, "{}");
        var nested = Directory.CreateDirectory(Path.Combine(_temp.Path, "src", "Api")).FullName;

        Assert.Equal(path, PresetFile.Find(nested));
    }

    [Fact]
    public void Export_ContainsOnlyThePreset()
    {
        var file = new PresetFile();
        file.Presets["a"] = new PublishPreset([]);
        file.Presets["b"] = new PublishPreset([]);

        Assert.Equal(["a"], file.Export("a").Presets.Keys);
    }

    [Fact]
    public async Task ResolveAsync_LoadsProjectsAndProfilesAndReportsProblems()
    {
        var api = _temp.CreateProject("src/Api/Api.csproj", sdk: "Microsoft.NET.Sdk.Web");
        var folder = new PublishProfile("Folder");
        folder[ProfileProperties.WebPublishMethod] = "FileSystem";
        PublishProfileStore.Save(Path.GetDirectoryName(api)!, folder);
        _temp.CreateProject("src/Worker/Worker.csproj", sdk: "Microsoft.NET.Sdk.Worker");

        var preset = new PublishPreset(
        [
            new PresetProject("src/Api/Api.csproj", "folder"),
            new PresetProject("src/Worker/Worker.csproj", "Missing"),
            new PresetProject("src/Gone/Gone.csproj", "Folder")
        ]);

        var resolved = await _resolver.ResolveAsync(preset, _temp.Path, TestContext.Current.CancellationToken);

        var (project, profile, _) = Assert.Single(resolved.Items);
        Assert.Equal("Api", project.Name);
        Assert.Equal("Folder", profile.Name);
        Assert.False(resolved.IsValid);
        Assert.Contains(resolved.Problems, p => p.Reason.Contains("no publish profile named 'Missing'"));
        Assert.Contains(resolved.Problems, p => p.Reason.StartsWith("project not found"));
    }

    [Fact]
    public void CreateEntry_UsesForwardSlashRelativePaths()
    {
        var project = new ProjectInfo { Name = "Api", FilePath = Path.Combine(_temp.Path, "src", "Api", "Api.csproj"), Kind = ProjectKind.Web };

        Assert.Equal(new PresetProject("src/Api/Api.csproj", "Prod"), PresetResolver.CreateEntry(project, new PublishProfile("Prod"), _temp.Path));
    }

    public void Dispose() => _temp.Dispose();
}
