using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Settings for the Zip file target.
/// </summary>
public sealed class ZipFileProfilePage(IAnsiConsole console)
{
    /// <returns>
    /// An unsaved folder profile with <c>PubwizZipFile</c> set. Visual Studio ignores that property and publishes
    /// the same profile to its folder.
    /// </returns>
    public PublishProfile Create(ProjectInfo project)
    {
        var targetFramework = CommonPrompts.ChooseTargetFramework(console, project);
        var configuration = CommonPrompts.ChooseConfiguration(console);

        var zipFile = console.Ask("Zip file", hint: "relative to the project folder", defaultValue: $"bin/{configuration}/{project.Name}.zip");

        var folder = $"bin/{configuration}/{targetFramework}/publish/";
        var profile = new PublishProfile("ZipProfile");
        profile[ProfileProperties.WebPublishMethod] = "FileSystem";
        profile[ProfileProperties.PublishProtocol] = "FileSystem";
        profile[ProfileProperties.PublishProvider] = "FileSystem";
        profile[ProfileProperties.PublishUrl] = folder;
        profile[ProfileProperties.PublishDir] = folder;
        profile[ProfileProperties.PubwizZipFile] = zipFile;
        profile[ProfileProperties.LastUsedBuildConfiguration] = configuration;
        profile[ProfileProperties.LastUsedPlatform] = "Any CPU";
        profile[ProfileProperties.TargetFramework] = project.TargetFrameworks.Count > 1 ? targetFramework : null;
        profile[ProfileProperties.ExcludeAppData] = project.Kind is ProjectKind.Web ? "false" : null;
        return profile;
    }
}
