using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets.Azure;
using Pubwiz.WebDeploy;

namespace Pubwiz.Core.Targets.WebDeploy;

/// <summary>
/// Other ways to reach a Web Deploy server when msdeploy.exe can't run here (Linux, macOS, or Windows without Web Deploy).
/// </summary>
public static class WebDeployAlternatives
{
    /// <summary>
    /// <c>true</c> for an on-premises Web Deploy profile that can't publish from this machine: msdeploy.exe isn't
    /// installed and the built-in client can't take over (it needs a Basic-auth user name and password).
    /// (Azure Web Deploy profiles always can: they go through Kudu.)
    /// </summary>
    public static bool AreNeeded(PublishProfile profile) =>
        profile.Method == PublishMethod.MSDeploy && !AzureProfile.IsAppService(profile)
        && MsDeployLocator.Find() is null && !WebDeployProfile.CanUseBuiltInClient(profile);

    /// <summary>
    /// The targets to suggest, best first.
    /// </summary>
    /// <param name="endpoint">The server, when known (an existing profile); <c>null</c> while creating one.</param>
    public static IReadOnlyList<PublishTargetType> SuggestFor(WebDeployEndpoint? endpoint) => endpoint switch
    {
        null => [PublishTargetType.AzureAppService, PublishTargetType.Ftp, PublishTargetType.Sftp, PublishTargetType.ZipFile],
        { IsAzure: true } => [PublishTargetType.AzureAppService],
        _ => [PublishTargetType.Ftp, PublishTargetType.Sftp, PublishTargetType.ZipFile]
    };

    /// <summary>The profile's server, or <c>null</c> when it has no usable service URL or site.</summary>
    public static WebDeployEndpoint? TryGetEndpoint(PublishProfile profile)
    {
        try
        {
            return WebDeployProfile.GetEndpoint(profile);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
