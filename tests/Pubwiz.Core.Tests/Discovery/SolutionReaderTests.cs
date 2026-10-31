using Pubwiz.Core.Discovery;
using Pubwiz.Core.Tests.TestHelpers;

namespace Pubwiz.Core.Tests.Discovery;

public sealed class SolutionReaderTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    [Fact]
    public async Task ReadProjectsAsync_ReadsSlnxWithSolutionFolders()
    {
        var api = _temp.CreateProject("src/Api/Api.csproj");
        var tests = _temp.CreateProject("tests/Api.Tests/Api.Tests.csproj");
        var solution = _temp.CreateFile("App.slnx", """
            <Solution>
              <Folder Name="/src/">
                <Project Path="src/Api/Api.csproj" />
              </Folder>
              <Project Path="tests/Api.Tests/Api.Tests.csproj" />
            </Solution>
            """);

        var projects = await SolutionReader.ReadProjectsAsync(solution, TestContext.Current.CancellationToken);

        Assert.Contains(new SolutionProject(api, "/src/"), projects);
        Assert.Contains(new SolutionProject(tests, null), projects);
    }

    [Fact]
    public async Task ReadProjectsAsync_ReadsClassicSlnWithBackslashPaths()
    {
        var api = _temp.CreateProject("src/Api/Api.csproj");
        var solution = _temp.CreateFile("App.sln", """
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Api", "src\Api\Api.csproj", "{6F1D4B2A-3C1B-4D5E-9F00-1A2B3C4D5E6F}"
            EndProject
            Global
            EndGlobal
            """);

        var projects = await SolutionReader.ReadProjectsAsync(solution, TestContext.Current.CancellationToken);

        Assert.Equal([new SolutionProject(api, null)], projects);
    }

    [Fact]
    public async Task ReadProjectsAsync_SkipsProjectsThatDoNotExist()
    {
        var solution = _temp.CreateFile("App.slnx", """
            <Solution>
              <Project Path="missing/Missing.csproj" />
            </Solution>
            """);

        var projects = await SolutionReader.ReadProjectsAsync(solution, TestContext.Current.CancellationToken);

        Assert.Empty(projects);
    }

    public void Dispose() => _temp.Dispose();
}
