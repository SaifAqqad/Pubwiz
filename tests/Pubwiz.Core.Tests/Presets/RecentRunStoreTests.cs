using Pubwiz.Core.Presets;
using Pubwiz.Core.Tests.TestHelpers;

namespace Pubwiz.Core.Tests.Presets;

public sealed class RecentRunStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    private RecentRunStore Store() => new(Path.Combine(_temp.Path, "config", "recent.json"));

    private RecentRun Run(DateTimeOffset at, string project = "Api") =>
        new(at, [new PresetProject(Path.Combine(_temp.Path, project, $"{project}.csproj"), "Production", 1)], "Release", 2);

    [Fact]
    public void SaveThenFind_RemembersTheRunPerStartFolder()
    {
        var store = Store();
        var run = Run(DateTimeOffset.Now);

        store.Save(_temp.Path, run);

        var found = Store().Find(_temp.Path + Path.DirectorySeparatorChar);
        Assert.NotNull(found);
        Assert.Equal(run.Projects, found.Projects);
        Assert.Equal("Release", found.Configuration);
        Assert.Equal(2, found.Parallel);
        Assert.Null(Store().Find(Path.Combine(_temp.Path, "other")));
    }

    [Fact]
    public void Save_ReplacesTheRunForTheSameFolder()
    {
        var store = Store();
        store.Save(_temp.Path, Run(DateTimeOffset.Now.AddDays(-1), "Api"));
        store.Save(_temp.Path, Run(DateTimeOffset.Now, "Web"));

        Assert.EndsWith("Web.csproj", store.Find(_temp.Path)!.Projects[0].Path);
    }

    [Fact]
    public void Save_KeepsOnlyTheNewestRuns()
    {
        var store = Store();
        var start = DateTimeOffset.Now.AddDays(-100);
        for (var i = 0; i <= RecentRunStore.MaxRuns; i++)
        {
            store.Save(Path.Combine(_temp.Path, $"folder{i}"), Run(start.AddHours(i)));
        }

        Assert.Null(store.Find(Path.Combine(_temp.Path, "folder0")));
        Assert.NotNull(store.Find(Path.Combine(_temp.Path, $"folder{RecentRunStore.MaxRuns}")));
    }

    [Theory]
    [InlineData("{ nope")]
    [InlineData("""{ "runs": { "/x": null } }""")]
    [InlineData("""{ "runs": { "PLACEHOLDER": { "publishedAt": "2026-01-01T00:00:00Z", "projects": [ { "profile": "P" } ] } } }""")]
    [InlineData("""{ "runs": { "PLACEHOLDER": { "publishedAt": "2026-01-01T00:00:00Z", "projects": [ { "path": "relative/App.csproj", "profile": "P" } ] } } }""")]
    public void Find_IgnoresAnInvalidFile(string json)
    {
        var store = Store();
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, json.Replace("PLACEHOLDER", _temp.Path.Replace("\\", "\\\\")));

        Assert.Null(store.Find(_temp.Path));
    }

    public void Dispose() => _temp.Dispose();
}
