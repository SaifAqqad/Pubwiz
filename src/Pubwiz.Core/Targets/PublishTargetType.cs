namespace Pubwiz.Core.Targets;

/// <summary>
/// Every place Pubwiz can publish to.
/// </summary>
public enum PublishTargetType
{
    Folder,
    ZipFile,
    WebDeploy,
    Ftp,
    Sftp,
    AzureAppService,
    AzureAppServiceContainer,
    AzureWebJob,
    AzureFunctions,
    AzureContainerApps,
    AzureStaticWebApp,
    ContainerRegistry,
    LocalContainer,
    ContainerTarball,
    MauiAndroid,
    MauiIos
}
