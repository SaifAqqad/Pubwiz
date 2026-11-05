using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Publishing.Steps;
using Pubwiz.Core.Targets.Azure;

namespace Pubwiz.Core.Targets.Containers;

/// <summary>
/// Builds a container image with the .NET SDK or a Dockerfile, and sends it to one destination:
/// the local Docker/Podman, a tarball, a registry, or a registry plus an Azure app that runs it.
/// </summary>
/// <remarks>Registered once per destination (see <see cref="ContainerProfile.Destination"/>) so jobs show the right target.</remarks>
public sealed class ContainerTarget(DotnetCli dotnet, ContainerCli containers, AzCli az, PublishTargetType destination) : IPublishTarget
{
    /// <summary>The destinations this target can be registered for.</summary>
    public static IReadOnlyList<PublishTargetType> Destinations { get; } =
    [
        PublishTargetType.LocalContainer,
        PublishTargetType.ContainerTarball,
        PublishTargetType.ContainerRegistry,
        PublishTargetType.AzureAppServiceContainer,
        PublishTargetType.AzureContainerApps
    ];

    public PublishTargetType Type => destination;

    public bool CanPublish(PublishProfile profile) =>
        ContainerProfile.IsContainerProfile(profile) && ContainerProfile.Read(profile, projectName: "app").Destination == destination;

    /// <summary>Only registries with a user name in the profile need a password; ACR uses az, others existing docker logins.</summary>
    public bool RequiresPassword(PublishProfile profile) =>
        destination is not (PublishTargetType.LocalContainer or PublishTargetType.ContainerTarball) && profile[ProfileProperties.UserName] is not null;

    public IReadOnlyList<PublishStep> CreateSteps(PublishRequest request)
    {
        var container = ContainerProfile.Read(request.Profile, request.Project.Name);
        var pushes = container.Registry is not null && container.ArchivePath is null;
        var archivePath = container.ArchivePath is null ? null : ResolveArchivePath(request, container);

        var steps = new List<PublishStep>();
        if (container.Builder == ContainerBuilder.Dockerfile)
        {
            var tool = container.LocalTool ?? ContainerTool.Docker;
            if (pushes)
            {
                steps.Add(new RegistrySignInStep(az, containers, container, tool));
            }

            steps.Add(new DockerBuildStep(containers, tool, container));
            if (pushes)
            {
                steps.Add(new DockerPushStep(containers, tool, container));
            }
            else if (archivePath is not null)
            {
                steps.Add(new DockerSaveStep(containers, tool, container, archivePath));
            }
        }
        else
        {
            if (pushes)
            {
                steps.Add(new RegistrySignInStep(az, containers, container, dockerfileTool: null));
            }

            steps.Add(CreateSdkPublishStep(request, container, archivePath));
        }

        if (container.AzureHost is not null)
        {
            var (resourceGroup, appName) = GetAzureApp(request.Profile);
            steps.Add(new AzureImageUpdateStep(az, container, resourceGroup, appName));
        }

        steps.Add(archivePath is not null
            ? new SetFileDestinationStep(archivePath, ".tar.gz", ".tgz", ".tar")
            : new SetDestinationStep(string.Join(", ", container.ImageNames)));
        return steps;
    }

    /// <summary>
    /// The app an image is deployed to: Visual Studio's <c>ResourceId</c> and <c>ResourceGroup</c>, or the
    /// <c>PubwizAzure*</c> properties of profiles from earlier Pubwiz versions.
    /// </summary>
    private static (string ResourceGroup, string AppName) GetAzureApp(PublishProfile profile)
    {
        var resourceId = profile[ProfileProperties.ResourceId];
        var resourceGroup = profile[ProfileProperties.PubwizAzureResourceGroup] ?? profile[ProfileProperties.ResourceGroup] ?? AzureResourceIds.GetResourceGroup(resourceId);
        var appName = profile[ProfileProperties.PubwizAzureAppName] ?? AzureResourceIds.GetName(resourceId);

        return resourceGroup is null || appName is null
            ? throw new ArgumentException($"The profile '{profile.Name}' has no {ProfileProperties.ResourceId} of the Azure app to update.")
            : (resourceGroup, appName);
    }

    /// <summary>
    /// <c>dotnet publish -t:PublishContainer</c> with the profile. <c>EnableSdkContainerSupport</c> is needed for
    /// console and worker projects; web projects have it on by default.
    /// </summary>
    /// <remarks>
    /// The registry and repository are passed too, so the SDK pushes exactly where Pubwiz shows, whichever
    /// property names the profile uses (e.g. Visual Studio's <c>RegistryUrl</c>) and whatever the SDK would default to.
    /// </remarks>
    private DotnetPublishStep CreateSdkPublishStep(PublishRequest request, ContainerProfile container, string? archivePath)
    {
        var properties = new Dictionary<string, string>
        {
            ["EnableSdkContainerSupport"] = "true",
            [ProfileProperties.ContainerRepository] = container.Repository
        };

        if (archivePath is not null)
        {
            properties[ProfileProperties.ContainerArchiveOutputPath] = archivePath;
        }
        else if (container.Registry is not null)
        {
            properties[ProfileProperties.ContainerRegistry] = container.Registry;
        }

        var options = new DotnetPublishOptions(request.Project.FilePath)
        {
            Configuration = request.EffectiveConfiguration,
            ProfileName = request.Profile.Name,
            TargetFramework = request.Profile[ProfileProperties.TargetFramework],
            RuntimeIdentifier = request.Profile[ProfileProperties.RuntimeIdentifier],
            Targets = ["PublishContainer"],
            Properties = properties,
            Verbose = request.Verbose
        };

        var outputDirectory = Path.Combine(request.Project.Directory, "bin", request.EffectiveConfiguration);
        return new DotnetPublishStep(dotnet, options, outputDirectory);
    }

    /// <summary>
    /// The tarball path, relative to the project folder. A folder (ending with a separator) gets
    /// <c>{repository}.tar.gz</c>, like the SDK does.
    /// </summary>
    internal static string ResolveArchivePath(PublishRequest request, ContainerProfile container)
    {
        var path = ProfileVariables.Expand(container.ArchivePath!, request).Replace('\\', '/');
        var full = Path.GetFullPath(Path.Combine(request.Project.Directory, path));
        return path.EndsWith('/') || Directory.Exists(full)
            ? Path.Combine(full, $"{container.Repository.Replace('/', '-')}.tar.gz")
            : full;
    }
}
