using Pubwiz.Core.Discovery;
using Pubwiz.Core.Infrastructure.Platform;

namespace Pubwiz.Core.Targets;

/// <summary>
/// Which publish targets are offered for each kind of project.
/// </summary>
public static class PublishTargetCatalog
{
    /// <summary>
    /// Targets that are implemented. Others are listed but marked as coming soon.
    /// </summary>
    private static readonly HashSet<PublishTargetType> Implemented =
    [
        PublishTargetType.Folder,
        PublishTargetType.ZipFile,
        PublishTargetType.WebDeploy,
        PublishTargetType.Ftp,
        PublishTargetType.Sftp,
        PublishTargetType.AzureAppService,
        PublishTargetType.AzureFunctions,
        PublishTargetType.AzureWebJob,
        PublishTargetType.AzureAppServiceContainer,
        PublishTargetType.AzureContainerApps,
        PublishTargetType.AzureStaticWebApp,
        PublishTargetType.AwsElasticBeanstalk,
        PublishTargetType.AwsEcs,
        PublishTargetType.AwsLambda,
        PublishTargetType.ContainerRegistry,
        PublishTargetType.LocalContainer,
        PublishTargetType.ContainerTarball,
        PublishTargetType.MauiAndroid,
        PublishTargetType.MauiIos
    ];

    private static readonly PublishTarget[] AllTargets =
    [
        new(PublishTargetType.Folder, PublishTargetGroup.Local, "Folder", "Publish to a local or network folder"),
        new(PublishTargetType.ZipFile, PublishTargetGroup.Local, "Zip file", "Publish to a .zip to copy to a server yourself"),
        new(PublishTargetType.WebDeployPackage, PublishTargetGroup.Local, "Web Deploy package", "Create a package to import into IIS later"),
        new(PublishTargetType.WebDeploy, PublishTargetGroup.Servers, "IIS (Web Deploy)", "Deploy to an IIS server with Web Deploy (msdeploy)"),
        new(PublishTargetType.Ftp, PublishTargetGroup.Servers, "FTP / FTPS", "Upload to an FTP or FTPS server"),
        new(PublishTargetType.Sftp, PublishTargetGroup.Servers, "SFTP", "Upload over SSH"),
        new(PublishTargetType.AzureAppService, PublishTargetGroup.Azure, "Azure App Service", "Deploy to an App Service web app (Windows or Linux)"),
        new(PublishTargetType.AzureAppServiceContainer, PublishTargetGroup.Azure, "Azure App Service (container)", "Build an image and run it on App Service"),
        new(PublishTargetType.AzureWebJob, PublishTargetGroup.Azure, "Azure WebJob", "Run as a WebJob inside an App Service app"),
        new(PublishTargetType.AzureFunctions, PublishTargetGroup.Azure, "Azure Function App", "Deploy to an Azure Function App"),
        new(PublishTargetType.AzureContainerApps, PublishTargetGroup.Azure, "Azure Container Apps", "Build an image and run it on Container Apps"),
        new(PublishTargetType.AzureStaticWebApp, PublishTargetGroup.Azure, "Azure Static Web Apps", "Host the app's files on a Static Web App"),
        new(PublishTargetType.AwsElasticBeanstalk, PublishTargetGroup.Aws, "AWS Elastic Beanstalk", "Deploy to an Elastic Beanstalk environment (Linux or Windows)"),
        new(PublishTargetType.AwsEcs, PublishTargetGroup.Aws, "Amazon ECS", "Build an image, push it to ECR and run it on an ECS service"),
        new(PublishTargetType.AwsLambda, PublishTargetGroup.Aws, "AWS Lambda", "Update the code of a Lambda function"),
        new(PublishTargetType.ContainerRegistry, PublishTargetGroup.Containers, "Container registry", "Push an image to ACR, Docker Hub or any registry"),
        new(PublishTargetType.LocalContainer, PublishTargetGroup.Containers, "Local container", "Build an image into the local Docker or Podman"),
        new(PublishTargetType.ContainerTarball, PublishTargetGroup.Containers, "Container tarball", "Save the image as a .tar.gz file"),
        new(PublishTargetType.MauiAndroid, PublishTargetGroup.Mobile, "Android", "Signed .apk or .aab"),
        new(PublishTargetType.MauiIos, PublishTargetGroup.Mobile, "iOS", "Signed .ipa (requires macOS)")
    ];

    /// <summary>
    /// The targets offered for a kind of project, in display order.
    /// </summary>
    public static IReadOnlyList<PublishTarget> For(ProjectKind kind)
    {
        var types = GetTargetTypes(kind);
        return AllTargets
            .Where(target => types.Contains(target.Type))
            .Select(target => target with { UnavailableReason = GetUnavailableReason(target.Type) })
            .ToList();
    }

    /// <summary>The name shown to users, e.g. "Zip file" for <see cref="PublishTargetType.ZipFile"/>.</summary>
    public static string DisplayName(PublishTargetType type) =>
        AllTargets.FirstOrDefault(target => target.Type == type)?.DisplayName ?? type.ToString();

    private static HashSet<PublishTargetType> GetTargetTypes(ProjectKind kind) => kind switch
    {
        ProjectKind.Web =>
        [
            PublishTargetType.Folder, PublishTargetType.ZipFile, PublishTargetType.WebDeploy, PublishTargetType.WebDeployPackage, PublishTargetType.Ftp, PublishTargetType.Sftp,
            PublishTargetType.AzureAppService, PublishTargetType.AzureAppServiceContainer, PublishTargetType.AzureContainerApps,
            PublishTargetType.AwsElasticBeanstalk, PublishTargetType.AwsEcs, PublishTargetType.AwsLambda,
            PublishTargetType.ContainerRegistry, PublishTargetType.LocalContainer, PublishTargetType.ContainerTarball
        ],
        ProjectKind.BlazorWasm =>
        [
            PublishTargetType.Folder, PublishTargetType.ZipFile, PublishTargetType.Ftp, PublishTargetType.Sftp, PublishTargetType.AzureStaticWebApp, PublishTargetType.AzureAppService,
            PublishTargetType.ContainerRegistry, PublishTargetType.LocalContainer, PublishTargetType.ContainerTarball
        ],
        ProjectKind.Worker =>
        [
            PublishTargetType.Folder, PublishTargetType.ZipFile, PublishTargetType.Ftp, PublishTargetType.Sftp, PublishTargetType.AzureWebJob, PublishTargetType.AzureContainerApps,
            PublishTargetType.AwsEcs, PublishTargetType.ContainerRegistry, PublishTargetType.LocalContainer, PublishTargetType.ContainerTarball
        ],
        ProjectKind.Console =>
        [
            PublishTargetType.Folder, PublishTargetType.ZipFile, PublishTargetType.Ftp, PublishTargetType.Sftp, PublishTargetType.AzureWebJob,
            PublishTargetType.ContainerRegistry, PublishTargetType.LocalContainer, PublishTargetType.ContainerTarball
        ],
        ProjectKind.AzureFunctions =>
        [
            PublishTargetType.Folder, PublishTargetType.ZipFile, PublishTargetType.AzureFunctions, PublishTargetType.AzureContainerApps,
            PublishTargetType.ContainerRegistry, PublishTargetType.LocalContainer, PublishTargetType.ContainerTarball
        ],
        ProjectKind.AwsLambda => [PublishTargetType.Folder, PublishTargetType.ZipFile, PublishTargetType.AwsLambda],
        ProjectKind.Maui => [PublishTargetType.Folder, PublishTargetType.MauiAndroid, PublishTargetType.MauiIos],
        ProjectKind.Desktop => [PublishTargetType.Folder, PublishTargetType.ZipFile],
        _ => []
    };

    private static string? GetUnavailableReason(PublishTargetType type)
    {
        if (type is PublishTargetType.MauiIos && !OperatingSystemInfo.IsMacOS)
        {
            return "requires macOS";
        }

        return Implemented.Contains(type) ? null : "coming soon";
    }
}
