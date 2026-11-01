using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Publishing.Steps;

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

    public string? GetSetupError(PublishProfile profile) =>
        ProfileVariables.FindUnknown(profile[ProfileProperties.PublishUrl] ?? profile[ProfileProperties.PublishDir]) is { } unknown
            ? $"The publish folder uses $({unknown}), which Pubwiz can't fill in. Use a plain path, or one of $({string.Join("), $(", ProfileVariables.Known)})."
            : null;

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
    /// Visual Studio writes these with backslashes (normalized here) and MSBuild variables such as <c>$(Configuration)</c>
    /// (filled in, see <see cref="ProfileVariables"/>).
    /// </summary>
    internal static string GetOutputDirectory(PublishRequest request)
    {
        var folder = request.Profile[ProfileProperties.PublishUrl]
                     ?? request.Profile[ProfileProperties.PublishDir]
                     ?? Path.Combine("bin", request.EffectiveConfiguration, "publish");

        var normalized = ProfileVariables.Expand(folder, request).Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(request.Project.Directory, normalized)));
    }
}
