using Pubwiz.Core.Discovery;
using Pubwiz.Core.Tests.TestHelpers;

namespace Pubwiz.Core.Tests.Discovery;

public sealed class WorkspaceScannerTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    [Fact]
    public void Scan_PrefersSolutionsOverProjectsAtTheSameLevel()
    {
        var solution = _temp.CreateFile("App.slnx", "<Solution />");
        _temp.CreateProject("App.csproj");

        var result = WorkspaceScanner.Scan(_temp.Path);

        Assert.Equal([solution], result.SolutionPaths);
        Assert.Empty(result.ProjectPaths);
    }

    [Fact]
    public void Scan_ReturnsProjectsWhenThereIsNoSolution()
    {
        var api = _temp.CreateProject("Api.csproj");
        var worker = _temp.CreateProject("Worker.csproj");

        var result = WorkspaceScanner.Scan(_temp.Path);

        Assert.Empty(result.SolutionPaths);
        Assert.Equal([api, worker], result.ProjectPaths);
    }

    [Fact]
    public void Scan_StopsAtTheFirstLevelWithMatches()
    {
        var shallow = _temp.CreateProject("src/Api/Api.csproj");
        _temp.CreateFile("src/Api/nested/Other.sln");

        var result = WorkspaceScanner.Scan(_temp.Path);

        // src/Api is level 2, src/Api/nested is level 3 (too deep anyway), so only the project is found.
        Assert.Equal([shallow], result.ProjectPaths);
    }

    [Fact]
    public void Scan_FindsSolutionsOneLevelDown()
    {
        var solution = _temp.CreateFile("repo/App.sln");

        var result = WorkspaceScanner.Scan(_temp.Path);

        Assert.Equal([solution], result.SolutionPaths);
    }

    [Fact]
    public void Scan_DoesNotSearchDeeperThanMaxDepth()
    {
        _temp.CreateProject("a/b/c/Deep.csproj");

        var result = WorkspaceScanner.Scan(_temp.Path);

        Assert.True(result.IsEmpty);
    }

    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData(".git")]
    [InlineData("node_modules")]
    public void Scan_SkipsIgnoredFolders(string folder)
    {
        _temp.CreateProject($"{folder}/Hidden.csproj");

        var result = WorkspaceScanner.Scan(_temp.Path);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Scan_AcceptsAProjectFilePath()
    {
        var project = _temp.CreateProject("Api.csproj");

        var result = WorkspaceScanner.Scan(project);

        Assert.Equal([project], result.ProjectPaths);
    }

    [Fact]
    public void Scan_RejectsOtherFiles()
    {
        var file = _temp.CreateFile("readme.md");

        Assert.Throws<ArgumentException>(() => WorkspaceScanner.Scan(file));
    }

    public void Dispose() => _temp.Dispose();
}
