using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Settings for the Folder target.
/// </summary>
public sealed class FolderProfilePage(IAnsiConsole console)
{
    /// <returns>An unsaved profile with the same properties Visual Studio writes for folder publishing.</returns>
    public PublishProfile Create(ProjectInfo project)
    {
        var targetFramework = CommonPrompts.ChooseTargetFramework(console, project);
        var configuration = CommonPrompts.ChooseConfiguration(console);

        var folder = console.Ask("Publish to folder", defaultValue: $"bin/{configuration}/{targetFramework}/publish/");
        var deleteExisting = console.AskYesNo("Delete existing files in the folder before publishing?", defaultValue: false);

        // Both folder properties are written: web projects read PublishUrl, other projects read PublishDir.
        var profile = new PublishProfile("FolderProfile");
        profile[ProfileProperties.WebPublishMethod] = "FileSystem";
        profile[ProfileProperties.PublishProtocol] = "FileSystem";
        profile[ProfileProperties.PublishProvider] = "FileSystem";
        profile[ProfileProperties.PublishUrl] = folder;
        profile[ProfileProperties.PublishDir] = folder;
        profile[ProfileProperties.DeleteExistingFiles] = deleteExisting ? "true" : "false";
        profile[ProfileProperties.LastUsedBuildConfiguration] = configuration;
        profile[ProfileProperties.LastUsedPlatform] = "Any CPU";
        profile[ProfileProperties.TargetFramework] = project.TargetFrameworks.Count > 1 ? targetFramework : null;
        profile[ProfileProperties.ExcludeAppData] = project.Kind is ProjectKind.Web ? "false" : null;
        return profile;
    }
}
