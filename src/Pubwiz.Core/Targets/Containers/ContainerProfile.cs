using System.Text.RegularExpressions;
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
/// The container settings of a profile (<c>WebPublishMethod=Container</c>), stored with the property names of the SDK and
/// Visual Studio, so the profile also works with <c>dotnet publish -t:PublishContainer -p:PublishProfile=...</c> and in VS.
/// </summary>
public sealed partial record ContainerProfile(
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
    /// <param name="projectName">Gives the repository when the profile has none (see <see cref="ToRepositoryName"/>).</param>
    public static ContainerProfile Read(PublishProfile profile, string projectName)
    {
        // Several tags, one SDK tag, or Visual Studio's single tag.
        var tags = (profile[ProfileProperties.ContainerImageTags] ?? profile[ProfileProperties.ContainerImageTag] ?? profile[ProfileProperties.PublishImageTag] ?? "latest")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new ContainerProfile(
            Builder: string.Equals(profile[ProfileProperties.PubwizContainerBuilder], "Dockerfile", StringComparison.OrdinalIgnoreCase) ? ContainerBuilder.Dockerfile : ContainerBuilder.Sdk,
            Registry: ReadRegistry(profile),
            Repository: profile[ProfileProperties.ContainerRepository] ?? ToRepositoryName(projectName),
            Tags: tags.Length > 0 ? tags : ["latest"],
            ArchivePath: NullIfEmpty(profile[ProfileProperties.ContainerArchiveOutputPath]),
            LocalTool: Enum.TryParse<ContainerTool>(profile[ProfileProperties.LocalRegistry], ignoreCase: true, out var tool) ? tool : null,
            Dockerfile: profile[ProfileProperties.PubwizDockerfile] ?? "Dockerfile",
            DockerContext: profile[ProfileProperties.PubwizDockerContext] ?? ".",
            AzureHost: NullIfEmpty(profile[ProfileProperties.PubwizAzureContainerHost]));
    }

    /// <summary>
    /// Turns a project name into a valid repository name: lowercase letters and digits, with a dash in place of
    /// anything else, e.g. <c>Contoso.Shop.Api</c> becomes <c>contoso-shop-api</c>.
    /// </summary>
    public static string ToRepositoryName(string projectName)
    {
        var name = InvalidRepositoryCharacters().Replace(projectName.ToLowerInvariant(), "-").Trim('-');
        return name.Length > 0 ? name : "app";
    }

    /// <summary>
    /// Registry rules for repositories: lowercase parts of letters and digits, joined by <c>.</c>, <c>_</c>,
    /// <c>__</c> or dashes, with <c>/</c> between path parts, e.g. <c>contoso/shop-api</c>.
    /// </summary>
    public static bool IsValidRepository(string repository) => repository.Length <= 255 && RepositoryPattern().IsMatch(repository);

    /// <summary>Registry rules for tags: up to 128 letters, digits, <c>_</c>, <c>.</c> and <c>-</c>, not starting with <c>.</c> or <c>-</c>.</summary>
    public static bool IsValidTag(string tag) => TagPattern().IsMatch(tag);

    /// <summary>
    /// <c>ContainerRegistry</c>, or Visual Studio's <c>RegistryUrl</c>, which older profiles write as a URL
    /// (<c>https://registry.hub.docker.com/contoso</c>): the scheme and trailing slash are dropped.
    /// </summary>
    private static string? ReadRegistry(PublishProfile profile)
    {
        var registry = NullIfEmpty(profile[ProfileProperties.ContainerRegistry]) ?? NullIfEmpty(profile[ProfileProperties.RegistryUrl]);
        if (registry is null)
        {
            return null;
        }

        var schemeEnd = registry.IndexOf("://", StringComparison.Ordinal);
        return (schemeEnd >= 0 ? registry[(schemeEnd + 3)..] : registry).TrimEnd('/');
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex InvalidRepositoryCharacters();

    [GeneratedRegex("^[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*(?:/[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*)*$")]
    private static partial Regex RepositoryPattern();

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$")]
    private static partial Regex TagPattern();
}
