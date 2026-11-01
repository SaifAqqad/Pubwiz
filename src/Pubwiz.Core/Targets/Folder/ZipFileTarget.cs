using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Publishing.Steps;

namespace Pubwiz.Core.Targets.Folder;

/// <summary>
/// Publishes to a staging folder and zips it (<c>FileSystem</c> profiles with <c>PubwizZipFile</c>), for copying
/// to a server by hand. The zip holds the folder's contents, ready to extract into an IIS site folder.
/// </summary>
/// <remarks>Registered before <see cref="FolderTarget"/>, which publishes the other FileSystem profiles.</remarks>
public sealed class ZipFileTarget(DotnetCli dotnet) : IPublishTarget
{
    public PublishTargetType Type => PublishTargetType.ZipFile;

    public static bool IsZipProfile(PublishProfile profile) => profile[ProfileProperties.PubwizZipFile] is not null;

    public bool CanPublish(PublishProfile profile) => profile.Method == PublishMethod.FileSystem && IsZipProfile(profile);

    public bool RequiresPassword(PublishProfile profile) => false;

    public IReadOnlyList<PublishStep> CreateSteps(PublishRequest request)
    {
        var zipPath = GetZipPath(request);
        return
        [
            StagingPublish.CreateStep(dotnet, request),
            new PackageZipStep(zipPath),
            new SetFileDestinationStep(zipPath, ".zip")
        ];
    }

    /// <summary>The zip file's full path; relative paths are relative to the project folder.</summary>
    internal static string GetZipPath(PublishRequest request)
    {
        var path = ProfileVariables.Expand(request.Profile[ProfileProperties.PubwizZipFile]!, request)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        return Path.GetFullPath(Path.Combine(request.Project.Directory, path));
    }
}
