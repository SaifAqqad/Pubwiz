using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Targets.Folder;

namespace Pubwiz.Core.Tests.Publishing;

public class ProfileVariablesTests
{
    private static readonly string ProjectDirectory = Path.Combine(Path.GetTempPath(), "App");

    private static PublishRequest Request(string publishUrl, string? targetFramework = null)
    {
        var project = new ProjectInfo { Name = "App", FilePath = Path.Combine(ProjectDirectory, "App.csproj"), Kind = ProjectKind.Web, TargetFrameworks = ["net10.0"] };
        var profile = new PublishProfile("Folder");
        profile[ProfileProperties.WebPublishMethod] = "FileSystem";
        profile[ProfileProperties.PublishUrl] = publishUrl;
        profile[ProfileProperties.TargetFramework] = targetFramework;
        return new PublishRequest(project, profile) { Configuration = "Release" };
    }

    [Fact]
    public void FolderTarget_FillsInVisualStudioVariables()
    {
        var request = Request(@"bin\$(Configuration)\$(TargetFramework)\publish\");

        Assert.Equal(Path.Combine(ProjectDirectory, "bin", "Release", "net10.0", "publish"), FolderTarget.GetOutputDirectory(request));
    }

    [Fact]
    public void Expand_IsCaseInsensitiveAndUsesTheProfilesFramework()
    {
        var request = Request("x", targetFramework: "net9.0");

        Assert.Equal("out/net9.0/Release/App", ProfileVariables.Expand("out/$(targetframework)/$(CONFIGURATION)/$(ProjectName)", request));
    }

    [Fact]
    public void EffectiveConfiguration_ReadsVisualStudioConsoleFolderProfiles()
    {
        // Visual Studio writes Configuration instead of LastUsedBuildConfiguration for non-web projects.
        var project = new ProjectInfo { Name = "Worker", FilePath = Path.Combine(ProjectDirectory, "Worker.csproj"), Kind = ProjectKind.Console };
        var profile = new PublishProfile("FolderProfile");
        profile[ProfileProperties.PublishProtocol] = "FileSystem";
        profile[ProfileProperties.Configuration] = "Debug";

        Assert.Equal("Debug", new PublishRequest(project, profile).EffectiveConfiguration);
        Assert.Equal("Release", new PublishRequest(project, profile) { Configuration = "Release" }.EffectiveConfiguration);
    }

    [Fact]
    public void Expand_LeavesUnknownVariables()
    {
        Assert.Equal("$(SolutionDir)out", ProfileVariables.Expand("$(SolutionDir)out", Request("x")));
    }

    [Fact]
    public void FolderTarget_ReportsUnknownVariables()
    {
        var target = new FolderTarget(dotnet: null!);

        Assert.Contains("$(SolutionDir)", target.GetSetupError(Request(@"$(SolutionDir)publish\").Profile));
        Assert.Null(target.GetSetupError(Request(@"bin\$(Configuration)\publish\").Profile));
    }

    [Fact]
    public void ProfileValues_AreTrimmed()
    {
        var profile = new PublishProfile("Test");
        profile[ProfileProperties.PublishUrl] = "\n      https://contoso.scm.azurewebsites.net/\n    ";

        Assert.Equal("https://contoso.scm.azurewebsites.net/", profile[ProfileProperties.PublishUrl]);
    }
}
