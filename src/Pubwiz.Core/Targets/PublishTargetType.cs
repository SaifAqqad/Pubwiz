namespace Pubwiz.Core.Targets;

/// <summary>
/// Every place Pubwiz can publish to.
/// </summary>
public enum PublishTargetType
{
    Folder,
    WebDeploy,
    WebDeployPackage,
    Ftp,
    Sftp,
    AzureAppService,
    AzureAppServiceContainer,
    AzureWebJob,
    AzureFunctions,
    AzureContainerApps,
    ContainerRegistry,
    LocalContainer,
    ContainerTarball,
    MauiAndroid,
    MauiIos
}
