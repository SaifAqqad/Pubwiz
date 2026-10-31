using Pubwiz.Core.Containers;
using Pubwiz.Core.Profiles;

namespace Pubwiz.Core.Targets.Containers;

/// <summary>How a container image is built.</summary>
public enum ContainerBuilder
{
    /// <summary>The .NET SDK's built-in container support: no Dockerfile or daemon needed to build.</summary>
    Sdk,

    /// <summary>The project's Dockerfile, built with docker or podman.</summary>
    Dockerfile
}

/// <summary>
/// The container settings of a profile (<c>WebPublishMethod=Container</c>), stored with the SDK's own property names
/// so the profile also works with <c>dotnet publish -t:PublishContainer -p:PublishProfile=...</c>.
/// </summary>
public sealed record ContainerProfile(
    ContainerBuilder Builder,
    string? Registry,
    string Repository,
    IReadOnlyList<string> Tags,
    string? ArchivePath,
    ContainerTool? LocalTool,
    string Dockerfile,
    string DockerContext,
    string? AzureHost)
{
    /// <summary>Where this profile sends the image, which decides the target.</summary>
    public PublishTargetType Destination => this switch
    {
        { ArchivePath: not null } => PublishTargetType.ContainerTarball,
        { Registry: null } => PublishTargetType.LocalContainer,
        { AzureHost: "AppService" } => PublishTargetType.AzureAppServiceContainer,
        { AzureHost: "ContainerApp" } => PublishTargetType.AzureContainerApps,
        _ => PublishTargetType.ContainerRegistry
    };

    /// <summary><c>true</c> for Azure Container Registry, where az can provide a push token.</summary>
    public bool IsAzureRegistry => Registry is not null && Registry.Contains(".azurecr.", StringComparison.OrdinalIgnoreCase);

    /// <summary>The registry name for <c>az acr</c> commands, e.g. <c>contoso</c> for <c>contoso.azurecr.io</c>.</summary>
    public string? AzureRegistryName => IsAzureRegistry ? Registry![..Registry!.IndexOf('.')] : null;

    /// <summary>Full image names, one per tag, e.g. <c>contoso.azurecr.io/api:latest</c>.</summary>
    public IReadOnlyList<string> ImageNames => Tags.Select(tag => Registry is null ? $"{Repository}:{tag}" : $"{Registry}/{Repository}:{tag}").ToList();

    public static bool IsContainerProfile(PublishProfile profile) => profile.Method == PublishMethod.Container;

    /// <param name="profile">A container profile.</param>
    /// <param name="defaultRepository">Used when the profile has no repository (the SDK uses the assembly name).</param>
    public static ContainerProfile Read(PublishProfile profile, string defaultRepository)
    {
        var tags = (profile[ProfileProperties.ContainerImageTags] ?? "latest")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new ContainerProfile(
            Builder: string.Equals(profile[ProfileProperties.PubwizContainerBuilder], "Dockerfile", StringComparison.OrdinalIgnoreCase) ? ContainerBuilder.Dockerfile : ContainerBuilder.Sdk,
            Registry: NullIfEmpty(profile[ProfileProperties.ContainerRegistry]),
            Repository: profile[ProfileProperties.ContainerRepository] ?? defaultRepository.ToLowerInvariant(),
            Tags: tags.Length > 0 ? tags : ["latest"],
            ArchivePath: NullIfEmpty(profile[ProfileProperties.ContainerArchiveOutputPath]),
            LocalTool: Enum.TryParse<ContainerTool>(profile[ProfileProperties.LocalRegistry], ignoreCase: true, out var tool) ? tool : null,
            Dockerfile: profile[ProfileProperties.PubwizDockerfile] ?? "Dockerfile",
            DockerContext: profile[ProfileProperties.PubwizDockerContext] ?? ".",
            AzureHost: NullIfEmpty(profile[ProfileProperties.PubwizAzureContainerHost]));
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
