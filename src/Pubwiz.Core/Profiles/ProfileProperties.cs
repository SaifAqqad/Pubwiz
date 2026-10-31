namespace Pubwiz.Core.Profiles;

/// <summary>
/// Names of the well-known publish profile properties (as used by Visual Studio and the Web SDK).
/// </summary>
/// <seealso href="https://github.com/dotnet/sdk/tree/main/src/WebSdk#microsoftnetsdkpublish"/>
public static class ProfileProperties
{
    // Common
    public const string WebPublishMethod = nameof(WebPublishMethod);

    /// <summary>Used instead of <see cref="WebPublishMethod"/> by Visual Studio profiles of non-web projects.</summary>
    public const string PublishProtocol = nameof(PublishProtocol);

    public const string PublishProvider = nameof(PublishProvider);
    public const string LastUsedBuildConfiguration = nameof(LastUsedBuildConfiguration);
    public const string LastUsedPlatform = nameof(LastUsedPlatform);
    public const string TargetFramework = nameof(TargetFramework);
    public const string RuntimeIdentifier = nameof(RuntimeIdentifier);
    public const string SelfContained = nameof(SelfContained);
    public const string SiteUrlToLaunchAfterPublish = nameof(SiteUrlToLaunchAfterPublish);
    public const string LaunchSiteAfterPublish = nameof(LaunchSiteAfterPublish);
    public const string ExcludeAppData = "ExcludeApp_Data";
    public const string UserName = nameof(UserName);

    // Folder, Zip deploy, FTP
    public const string PublishUrl = nameof(PublishUrl);

    /// <summary>The publish folder of non-web projects (web projects use <see cref="PublishUrl"/>).</summary>
    public const string PublishDir = nameof(PublishDir);

    public const string DeleteExistingFiles = nameof(DeleteExistingFiles);
    public const string FtpPassiveMode = nameof(FtpPassiveMode);

    /// <summary>Folder on the FTP server, added to the <see cref="PublishUrl"/> path (Visual Studio FTP profiles).</summary>
    public const string FtpSitePath = nameof(FtpSitePath);

    // Web Deploy
    public const string MSDeployServiceURL = nameof(MSDeployServiceURL);
    public const string DeployIisAppPath = nameof(DeployIisAppPath);
    public const string MSDeployPublishMethod = nameof(MSDeployPublishMethod);
    public const string EnableMSDeployBackup = nameof(EnableMSDeployBackup);
    public const string SkipExtraFilesOnServer = nameof(SkipExtraFilesOnServer);
    public const string AllowUntrustedCertificate = nameof(AllowUntrustedCertificate);
    public const string AuthType = nameof(AuthType);

    /// <summary>The value of <see cref="PublishProvider"/> for Azure App Service.</summary>
    public const string AzureWebSiteProvider = "AzureWebSite";

    // Pubwiz-only settings (Visual Studio ignores unknown properties)

    /// <summary>The value of <see cref="PublishProvider"/> for SFTP profiles (with <c>WebPublishMethod=Custom</c>).</summary>
    public const string SftpProvider = "Sftp";

    /// <summary>FTP security override: Auto, None, Explicit or Implicit. By default it follows the URL scheme.</summary>
    public const string PubwizFtpSecurity = nameof(PubwizFtpSecurity);

    /// <summary>Upload app_offline.htm while uploading (true/false).</summary>
    public const string PubwizUseAppOffline = nameof(PubwizUseAppOffline);

    /// <summary>Private key file for SFTP; the password is then used as the key's passphrase, if any.</summary>
    public const string PubwizSshPrivateKeyFile = nameof(PubwizSshPrivateKeyFile);
}
