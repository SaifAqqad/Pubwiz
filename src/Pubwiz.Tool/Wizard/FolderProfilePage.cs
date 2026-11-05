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

        var profile = CreateFolderProfile("FolderProfile", project, folder, configuration, targetFramework);
        profile[ProfileProperties.DeleteExistingFiles] = deleteExisting ? "true" : "false";
        return profile;
    }

    /// <summary>
    /// A folder profile with the properties Visual Studio writes: web projects get <c>WebPublishMethod</c> and
    /// <c>PublishUrl</c>, other projects <c>PublishProtocol</c> and <c>PublishDir</c>.
    /// </summary>
    internal static PublishProfile CreateFolderProfile(string name, ProjectInfo project, string folder, string configuration, string targetFramework)
    {
        var profile = new PublishProfile(name);
        if (project.Kind is ProjectKind.Web)
        {
            profile[ProfileProperties.WebPublishMethod] = "FileSystem";
            profile[ProfileProperties.PublishProvider] = "FileSystem";
            profile[ProfileProperties.PublishUrl] = folder;
            profile[ProfileProperties.ExcludeAppData] = "false";
        }
        else
        {
            profile[ProfileProperties.PublishProtocol] = "FileSystem";
            profile[ProfileProperties.PublishDir] = folder;
        }

        profile[ProfileProperties.LastUsedBuildConfiguration] = configuration;
        profile[ProfileProperties.LastUsedPlatform] = "Any CPU";
        profile[ProfileProperties.TargetFramework] = project.TargetFrameworks.Count > 1 ? targetFramework : null;
        profile[ProfileProperties.TargetId] = VisualStudioTargetIds.Folder;
        return profile;
    }
}
