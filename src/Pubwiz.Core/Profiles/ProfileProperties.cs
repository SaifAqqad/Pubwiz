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

    // Azure WebJobs (same names as the Web SDK's OneDeploy task)

    /// <summary>The WebJob's name; when set, a Zip/One deploy profile deploys a WebJob instead of the app.</summary>
    public const string WebJobName = nameof(WebJobName);

    /// <summary><c>Continuous</c> or <c>Triggered</c>.</summary>
    public const string WebJobType = nameof(WebJobType);

    // SDK container publishing (https://learn.microsoft.com/dotnet/core/containers/publish-configuration)

    /// <summary>Where to push the image; empty means the local Docker/Podman.</summary>
    public const string ContainerRegistry = nameof(ContainerRegistry);

    /// <summary>The image name, e.g. <c>contoso/api</c>.</summary>
    public const string ContainerRepository = nameof(ContainerRepository);

    /// <summary>Semicolon-separated tags, e.g. <c>1.2.0;latest</c>.</summary>
    public const string ContainerImageTags = nameof(ContainerImageTags);

    /// <summary>Write the image to this .tar.gz file (or folder) instead of pushing it.</summary>
    public const string ContainerArchiveOutputPath = nameof(ContainerArchiveOutputPath);

    /// <summary><c>Docker</c> or <c>Podman</c>, for local images.</summary>
    public const string LocalRegistry = nameof(LocalRegistry);

    // Pubwiz-only settings (Visual Studio ignores unknown properties)

    /// <summary>The value of <see cref="PublishProvider"/> for SFTP profiles (with <c>WebPublishMethod=Custom</c>).</summary>
    public const string SftpProvider = "Sftp";

    /// <summary>FTP security override: Auto, None, Explicit or Implicit. By default it follows the URL scheme.</summary>
    public const string PubwizFtpSecurity = nameof(PubwizFtpSecurity);

    /// <summary>Upload app_offline.htm while uploading (true/false).</summary>
    public const string PubwizUseAppOffline = nameof(PubwizUseAppOffline);

    /// <summary>Private key file for SFTP; the password is then used as the key's passphrase, if any.</summary>
    public const string PubwizSshPrivateKeyFile = nameof(PubwizSshPrivateKeyFile);

    /// <summary><c>AzCli</c> to sign in with an az token instead of publishing credentials (the default when there's no user name).</summary>
    public const string PubwizAzureAuthentication = nameof(PubwizAzureAuthentication);

    /// <summary>The Azure subscription of the app, for <c>az</c> commands.</summary>
    public const string PubwizAzureSubscriptionId = nameof(PubwizAzureSubscriptionId);

    public const string PubwizAzureResourceGroup = nameof(PubwizAzureResourceGroup);

    public const string PubwizAzureAppName = nameof(PubwizAzureAppName);

    /// <summary><c>true</c> when the App Service runs on Linux (WebJobs then get a run.sh instead of run.cmd).</summary>
    public const string PubwizAzureLinux = nameof(PubwizAzureLinux);

    /// <summary>A CRON schedule for triggered WebJobs, written to settings.job, e.g. <c>0 */15 * * * *</c>.</summary>
    public const string PubwizWebJobSchedule = nameof(PubwizWebJobSchedule);

    /// <summary><c>Sdk</c> (default, dotnet publish -t:PublishContainer) or <c>Dockerfile</c> (docker/podman build).</summary>
    public const string PubwizContainerBuilder = nameof(PubwizContainerBuilder);

    /// <summary>The Dockerfile, relative to the project folder.</summary>
    public const string PubwizDockerfile = nameof(PubwizDockerfile);

    /// <summary>The docker build context, relative to the project folder (often the solution folder: <c>..</c>).</summary>
    public const string PubwizDockerContext = nameof(PubwizDockerContext);

    /// <summary>After pushing, point an Azure app at the image: <c>AppService</c> or <c>ContainerApp</c>.</summary>
    public const string PubwizAzureContainerHost = nameof(PubwizAzureContainerHost);
}
