using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Tool.Wizard;

namespace Pubwiz.Tool.Tests.Wizard;

public class FolderProfilePageTests
{
    private static ProjectInfo Project(ProjectKind kind) =>
        new() { Name = "App", FilePath = Path.GetFullPath("App/App.csproj"), Kind = kind, TargetFrameworks = ["net10.0"] };

    [Fact]
    public void CreateFolderProfile_WritesWhatVisualStudioWritesForWebProjects()
    {
        var profile = FolderProfilePage.CreateFolderProfile("FolderProfile", Project(ProjectKind.Web), "out/", "Release", "net10.0");

        Assert.Equal(
            ["WebPublishMethod", "PublishProvider", "PublishUrl", "ExcludeApp_Data", "LaunchSiteAfterPublish", "LastUsedBuildConfiguration", "LastUsedPlatform", "_TargetId"],
            profile.Properties.Select(p => p.Key));
    }

    [Fact]
    public void CreateFolderProfile_WritesWhatVisualStudioWritesForOtherProjects()
    {
        var profile = FolderProfilePage.CreateFolderProfile("FolderProfile", Project(ProjectKind.Console), "out/", "Debug", "net10.0");

        Assert.Equal(["Configuration", "Platform", "PublishDir", "PublishProtocol", "_TargetId"], profile.Properties.Select(p => p.Key));
        Assert.Equal("Debug", profile[ProfileProperties.Configuration]);
    }
}
