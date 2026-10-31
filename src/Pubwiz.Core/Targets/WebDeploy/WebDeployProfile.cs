using Pubwiz.Core.Profiles;
using Pubwiz.WebDeploy;

namespace Pubwiz.Core.Targets.WebDeploy;

/// <summary>
/// Reads Web Deploy settings from a publish profile.
/// </summary>
public static class WebDeployProfile
{
    /// <summary>The endpoint from <c>MSDeployServiceURL</c> and <c>DeployIisAppPath</c>.</summary>
    /// <exception cref="ArgumentException">The profile has no service URL or site.</exception>
    public static WebDeployEndpoint GetEndpoint(PublishProfile profile)
    {
        var serviceUrl = profile[ProfileProperties.MSDeployServiceURL];
        var iisPath = profile[ProfileProperties.DeployIisAppPath];

        if (string.IsNullOrWhiteSpace(serviceUrl) || string.IsNullOrWhiteSpace(iisPath))
        {
            throw new ArgumentException($"The profile '{profile.Name}' needs both {ProfileProperties.MSDeployServiceURL} and {ProfileProperties.DeployIisAppPath}.", nameof(profile));
        }

        return WebDeployEndpoint.Create(serviceUrl, iisPath);
    }
}
