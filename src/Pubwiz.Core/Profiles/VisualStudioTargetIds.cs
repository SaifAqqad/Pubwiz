namespace Pubwiz.Core.Profiles;

/// <summary>
/// Values of <see cref="ProfileProperties.TargetId"/> (<c>_TargetId</c>), as Visual Studio writes them.
/// Pubwiz writes one only for targets Visual Studio has, so VS opens those profiles on the right settings page.
/// </summary>
public static class VisualStudioTargetIds
{
    public const string Folder = "Folder";

    public const string IisWebDeploy = "IISWebDeploy";

    public const string Ftp = "FTP";

    /// <summary>Azure Container Registry, image built by the .NET SDK (web projects).</summary>
    public const string NetSdkAzureContainerRegistry = "NetSdkAzureContainerRegistry";

    /// <summary>Docker Hub, GitHub and other registries, image built by the .NET SDK (web projects).</summary>
    public const string NetSdkCustomContainerRegistry = "NetSdkCustomContainerRegistry";
}
