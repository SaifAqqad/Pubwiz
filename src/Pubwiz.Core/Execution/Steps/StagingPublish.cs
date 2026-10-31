using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Profiles;

namespace Pubwiz.Core.Execution.Steps;

/// <summary>
/// Publish options for targets that upload the app themselves (Web Deploy, FTP, Zip deploy, ...).
/// </summary>
/// <remarks>
/// The profile is still passed, so its settings (runtime, self-contained, web.config transforms, ...) apply,
/// but <c>WebPublishMethod</c> is overridden to <c>FileSystem</c>. Global properties win over profile properties,
/// so the SDK writes the app to a staging folder instead of trying to deploy it (for Web Deploy profiles it
/// would otherwise call msdeploy.exe, which only exists on Windows).
/// </remarks>
public static class StagingPublish
{
    /// <summary>The staging folder: <c>obj/pubwiz/&lt;profile&gt;/</c> inside the project.</summary>
    public static string GetStagingDirectory(PublishRequest request) =>
        Path.Combine(request.Project.Directory, "obj", "pubwiz", PublishProfileStore.ToValidName(request.Profile.Name));

    public static DotnetPublishOptions CreateOptions(PublishRequest request, string stagingDirectory)
    {
        var folder = stagingDirectory + Path.DirectorySeparatorChar;
        return new DotnetPublishOptions(request.Project.FilePath)
        {
            Configuration = request.EffectiveConfiguration,
            ProfileName = request.Profile.Name,
            TargetFramework = request.Profile[ProfileProperties.TargetFramework],
            RuntimeIdentifier = request.Profile[ProfileProperties.RuntimeIdentifier],
            Verbose = request.Verbose,
            Properties = new Dictionary<string, string>
            {
                [ProfileProperties.WebPublishMethod] = "FileSystem",
                [ProfileProperties.PublishProtocol] = "FileSystem",
                [ProfileProperties.PublishUrl] = folder,
                [ProfileProperties.PublishDir] = folder,
                [ProfileProperties.DeleteExistingFiles] = "true"
            }
        };
    }

    /// <summary>The "Build and publish" step writing to the staging folder.</summary>
    public static DotnetPublishStep CreateStep(DotnetCli dotnet, PublishRequest request)
    {
        var stagingDirectory = GetStagingDirectory(request);
        return new DotnetPublishStep(dotnet, CreateOptions(request, stagingDirectory), stagingDirectory);
    }
}
