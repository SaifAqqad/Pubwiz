using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Execution;
using Pubwiz.Core.Execution.Steps;
using Pubwiz.Core.Profiles;

namespace Pubwiz.Core.Targets.Folder;

/// <summary>
/// Publishes to a folder (<c>WebPublishMethod=FileSystem</c>): <c>dotnet publish</c> with the profile.
/// </summary>
/// <remarks>
/// The folder is passed explicitly as <c>PublishDir</c>: the Web SDK reads it from <c>PublishUrl</c>, but plain
/// SDK projects only honor <c>PublishDir</c>, so passing it makes both behave the same.
/// </remarks>
public sealed class FolderTarget(DotnetCli dotnet) : IPublishTarget
{
    public PublishTargetType Type => PublishTargetType.Folder;

    public bool CanPublish(PublishProfile profile) => profile.Method == PublishMethod.FileSystem && !ZipFileTarget.IsZipProfile(profile);

    public bool RequiresPassword(PublishProfile profile) => false;

    public IReadOnlyList<PublishStep> CreateSteps(PublishRequest request)
    {
        var outputDirectory = GetOutputDirectory(request);
        var options = new DotnetPublishOptions(request.Project.FilePath)
        {
            Configuration = request.EffectiveConfiguration,
            ProfileName = request.Profile.Name,
            TargetFramework = request.Profile[ProfileProperties.TargetFramework],
            RuntimeIdentifier = request.Profile[ProfileProperties.RuntimeIdentifier],
            Properties = new Dictionary<string, string> { [ProfileProperties.PublishDir] = outputDirectory + Path.DirectorySeparatorChar },
            Verbose = request.Verbose
        };

        return [new DotnetPublishStep(dotnet, options, outputDirectory), new VerifyOutputStep()];
    }

    /// <summary>
    /// The folder from <c>PublishUrl</c> (web) or <c>PublishDir</c> (other projects), relative to the project folder.
    /// Visual Studio writes these with backslashes, so they're normalized.
    /// </summary>
    internal static string GetOutputDirectory(PublishRequest request)
    {
        var folder = request.Profile[ProfileProperties.PublishUrl]
                     ?? request.Profile[ProfileProperties.PublishDir]
                     ?? Path.Combine("bin", request.EffectiveConfiguration, "publish");

        var normalized = folder.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(request.Project.Directory, normalized)));
    }
}
