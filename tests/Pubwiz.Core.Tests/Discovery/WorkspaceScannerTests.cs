using Pubwiz.Core.Discovery;
using Pubwiz.Core.Tests.TestHelpers;

namespace Pubwiz.Core.Tests.Discovery;

public sealed class WorkspaceScannerTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    [Fact]
    public async Task Scan_PrefersSolutionsOverProjectsAtTheSameLevel()
    {
        var solution = _temp.CreateFile("App.slnx", "<Solution />");
        _temp.CreateProject("App.csproj");

        var result = await ScanAsync(_temp.Path);

        Assert.Equal([solution], result.SolutionPaths);
        Assert.Empty(result.ProjectPaths);
    }

    [Fact]
    public async Task Scan_ReturnsProjectsWhenThereIsNoSolution()
    {
        var api = _temp.CreateProject("Api.csproj");
        var worker = _temp.CreateProject("Worker.csproj");

        var result = await ScanAsync(_temp.Path);

        Assert.Empty(result.SolutionPaths);
        Assert.Equal([api, worker], result.ProjectPaths);
    }

    [Fact]
    public async Task Scan_StopsAtTheFirstLevelWithMatches()
    {
        var shallow = _temp.CreateProject("src/Api/Api.csproj");
        _temp.CreateFile("src/Api/nested/Other.sln");

        var result = await ScanAsync(_temp.Path);

        // src/Api is level 2, src/Api/nested is level 3 (too deep anyway), so only the project is found.
        Assert.Equal([shallow], result.ProjectPaths);
    }

    [Fact]
    public async Task Scan_FindsSolutionsOneLevelDown()
    {
        var solution = _temp.CreateFile("repo/App.sln");

        var result = await ScanAsync(_temp.Path);

        Assert.Equal([solution], result.SolutionPaths);
    }

    [Fact]
    public async Task Scan_DoesNotSearchDeeperThanMaxDepth()
    {
        _temp.CreateProject("a/b/c/Deep.csproj");

        var result = await ScanAsync(_temp.Path);

        Assert.True(result.IsEmpty);
    }

    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData(".git")]
    [InlineData("node_modules")]
    public async Task Scan_SkipsIgnoredFolders(string folder)
    {
        _temp.CreateProject($"{folder}/Hidden.csproj");

        var result = await ScanAsync(_temp.Path);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public async Task Scan_AcceptsAProjectFilePath()
    {
        var project = _temp.CreateProject("Api.csproj");

        var result = await ScanAsync(project);

        Assert.Equal([project], result.ProjectPaths);
    }

    [Fact]
    public async Task Scan_RejectsOtherFiles()
    {
        var file = _temp.CreateFile("readme.md");

        await Assert.ThrowsAsync<ArgumentException>(() => ScanAsync(file));
    }

    [Fact]
    public async Task Scan_FindsAParentSolutionThatIncludesTheStartFolder()
    {
        CreateRepositoryRoot();
        _temp.CreateProject("src/Api/Api.csproj");
        var solution = CreateSolution("App.slnx", "src/Api/Api.csproj");

        var result = await ScanAsync(System.IO.Path.Combine(_temp.Path, "src", "Api"));

        Assert.Equal([solution], result.SolutionPaths);
    }

    [Fact]
    public async Task Scan_FromAProjectsFolder_ReportsThatProjectToo()
    {
        CreateRepositoryRoot();
        var api = _temp.CreateProject("src/Api/Api.csproj");
        _temp.CreateProject("src/Web/Web.csproj");
        CreateSolution("App.slnx", "src/Api/Api.csproj", "src/Web/Web.csproj");

        var result = await ScanAsync(System.IO.Path.Combine(_temp.Path, "src", "Api"));

        Assert.Equal([api], result.StartProjects);
    }

    [Fact]
    public async Task Scan_FromAFolderAboveTheProjects_ReportsNoStartProjects()
    {
        CreateRepositoryRoot();
        _temp.CreateProject("src/Api/Api.csproj");
        CreateSolution("App.slnx", "src/Api/Api.csproj");

        var result = await ScanAsync(System.IO.Path.Combine(_temp.Path, "src"));

        Assert.Empty(result.StartProjects);
    }

    [Fact]
    public async Task Scan_LeavesOutAProjectTheSolutionDoesNotInclude()
    {
        CreateRepositoryRoot();
        _temp.CreateProject("src/Api/Api.csproj");
        _temp.CreateProject("src/Api/Scratch.csproj");
        CreateSolution("App.slnx", "src/Api/Api.csproj");

        var result = await ScanAsync(System.IO.Path.Combine(_temp.Path, "src", "Api"));

        Assert.Equal(["Api.csproj"], result.StartProjects.Select(System.IO.Path.GetFileName));
    }

    [Fact]
    public async Task Scan_PrefersTheNearestParentSolution()
    {
        CreateRepositoryRoot();
        _temp.CreateProject("src/Api/Api.csproj");
        CreateSolution("All.slnx", "src/Api/Api.csproj");
        var nearest = CreateSolution("src/Src.slnx", "Api/Api.csproj");

        var result = await ScanAsync(System.IO.Path.Combine(_temp.Path, "src", "Api"));

        Assert.Equal([nearest], result.SolutionPaths);
    }

    [Fact]
    public async Task Scan_IgnoresParentSolutionsThatDoNotIncludeTheStartFolder()
    {
        CreateRepositoryRoot();
        _temp.CreateProject("src/Api/Api.csproj");
        CreateSolution("App.slnx", "src/Api/Api.csproj");
        var tool = _temp.CreateProject("tools/Tool/Tool.csproj");

        var result = await ScanAsync(System.IO.Path.Combine(_temp.Path, "tools", "Tool"));

        Assert.Empty(result.SolutionPaths);
        Assert.Equal([tool], result.ProjectPaths);
    }

    [Fact]
    public async Task Scan_FallsBackToTheRepositorySolutionWhenNothingIsBelow()
    {
        CreateRepositoryRoot();
        _temp.CreateProject("src/Api/Api.csproj");
        var solution = CreateSolution("App.slnx", "src/Api/Api.csproj");
        Directory.CreateDirectory(System.IO.Path.Combine(_temp.Path, "docs"));

        var result = await ScanAsync(System.IO.Path.Combine(_temp.Path, "docs"));

        Assert.Equal([solution], result.SolutionPaths);
    }

    [Fact]
    public async Task Scan_DoesNotSearchAboveTheRepositoryRoot()
    {
        Directory.CreateDirectory(System.IO.Path.Combine(_temp.Path, "repo", ".git"));
        var project = _temp.CreateProject("repo/src/Api/Api.csproj");
        CreateSolution("Outside.slnx", "repo/src/Api/Api.csproj");

        var result = await ScanAsync(System.IO.Path.Combine(_temp.Path, "repo", "src", "Api"));

        Assert.Empty(result.SolutionPaths);
        Assert.Equal([project], result.ProjectPaths);
    }

    /// <summary>Stops the parent search at the temp folder, so solutions elsewhere on the machine can't affect a test.</summary>
    private void CreateRepositoryRoot() => Directory.CreateDirectory(System.IO.Path.Combine(_temp.Path, ".git"));

    private string CreateSolution(string relativePath, params string[] projects) =>
        _temp.CreateFile(relativePath, $"<Solution>{string.Concat(projects.Select(p => $"<Project Path=\"{p}\" />"))}</Solution>");

    private static Task<WorkspaceScanResult> ScanAsync(string path) => WorkspaceScanner.ScanAsync(path, TestContext.Current.CancellationToken);

    public void Dispose() => _temp.Dispose();
}
