using Pubwiz.Core.Discovery;
using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Tests.TestHelpers;

namespace Pubwiz.Core.Tests.Discovery;

/// <summary>
/// Runs the real <c>dotnet msbuild</c> against small project files.
/// </summary>
public sealed class ProjectInspectorTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly ProjectInspector _inspector = new(new DotnetCli(new ProcessRunner(), DotnetLocator.Find()!));

    [Fact]
    public async Task InspectAsync_DetectsKindsAndTargetFrameworks()
    {
        var web = _temp.CreateProject("Web/Web.csproj", sdk: "Microsoft.NET.Sdk.Web");
        var worker = _temp.CreateProject("Worker/Worker.csproj", sdk: "Microsoft.NET.Sdk.Worker");
        var library = _temp.CreateProject("Lib/Lib.csproj", properties: "<TargetFrameworks>net8.0;net10.0</TargetFrameworks>");

        var projects = await _inspector.InspectAsync(
            [new SolutionProject(web, "/src/"), new SolutionProject(worker, null), new SolutionProject(library, null)],
            TestContext.Current.CancellationToken);

        Assert.Equal(["Web", "Worker", "Lib"], projects.Select(p => p.Name));
        Assert.Equal([ProjectKind.Web, ProjectKind.Worker, ProjectKind.Library], projects.Select(p => p.Kind));
        Assert.Equal("/src/", projects[0].SolutionFolder);
        Assert.Equal(["net8.0", "net10.0"], projects[2].TargetFrameworks);
        Assert.All(projects, p => Assert.Null(p.EvaluationError));
    }

    [Fact]
    public async Task InspectAsync_ReportsBrokenProjectsInsteadOfThrowing()
    {
        var broken = _temp.CreateFile("Broken/Broken.csproj", "<Project><NotClosed>");

        var project = await _inspector.InspectAsync(new SolutionProject(broken, null), TestContext.Current.CancellationToken);

        Assert.NotNull(project.EvaluationError);
        Assert.False(project.IsPublishable);
    }

    public void Dispose() => _temp.Dispose();
}
