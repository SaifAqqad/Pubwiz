namespace Pubwiz.Core.Profiles;

/// <summary>
/// How a profile publishes, from the <c>WebPublishMethod</c> property of a .pubxml file.
/// </summary>
public enum PublishMethod
{
    Unknown,

    /// <summary>Publish to a local or network folder.</summary>
    FileSystem,

    /// <summary>IIS Web Deploy (msdeploy).</summary>
    MSDeploy,

    /// <summary>A Web Deploy package (.zip) to deploy later.</summary>
    Package,

    /// <summary>Kudu zip deploy (Azure App Service).</summary>
    ZipDeploy,

    /// <summary>Kudu "one deploy" publish API (Azure App Service, WebJobs).</summary>
    OneDeploy,

    /// <summary>FTP or FTPS upload.</summary>
    FTP,

    /// <summary>Container image (SDK container support).</summary>
    Container,

    /// <summary>Provider-specific publishing, decided by <c>PublishProvider</c>.</summary>
    Custom
}

public static class PublishMethodParser
{
    public static PublishMethod Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "filesystem" => PublishMethod.FileSystem,
        "msdeploy" => PublishMethod.MSDeploy,
        "package" => PublishMethod.Package,
        "zipdeploy" => PublishMethod.ZipDeploy,
        "onedeploy" => PublishMethod.OneDeploy,
        "ftp" => PublishMethod.FTP,
        "container" => PublishMethod.Container,
        "custom" => PublishMethod.Custom,
        _ => PublishMethod.Unknown
    };
}
