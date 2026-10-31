namespace Pubwiz.Core.Profiles;

/// <summary>
/// One <c>&lt;publishProfile&gt;</c> entry of a .publishsettings file.
/// </summary>
/// <param name="ProfileName">e.g. <c>my-site - Web Deploy</c>.</param>
/// <param name="Method">The <c>publishMethod</c> attribute: MSDeploy, FTP or ZipDeploy.</param>
/// <param name="PublishUrl">The server: <c>host:port</c> for Web Deploy, <c>ftp://...</c> for FTP, the SCM host for Zip deploy.</param>
/// <param name="SiteName">The IIS site (<c>msdeploySite</c>), for Web Deploy.</param>
/// <param name="UserName">Deployment user name.</param>
/// <param name="Password">Deployment password (a secret: never written to .pubxml).</param>
/// <param name="DestinationAppUrl">The public URL of the site.</param>
/// <param name="FtpPassiveMode">Whether FTP should use passive mode.</param>
public sealed record PublishSettingsEntry(
    string ProfileName,
    PublishMethod Method,
    string PublishUrl,
    string? SiteName,
    string? UserName,
    string? Password,
    string? DestinationAppUrl,
    bool FtpPassiveMode)
{
    /// <summary>
    /// <c>true</c> for Azure App Service sites (as opposed to a self-hosted IIS server).
    /// </summary>
    public bool IsAzure => PublishUrl.Contains(".azurewebsites.", StringComparison.OrdinalIgnoreCase)
                           || PublishUrl.Contains(".scm.", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A profile created from a .publishsettings entry, with its password kept separately.
/// </summary>
public sealed record ImportedProfile(PublishProfile Profile, string? Password);
