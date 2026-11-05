using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets;
using Pubwiz.Core.Targets.Azure;
using Pubwiz.Core.Targets.Containers;
using Pubwiz.Tool.Azure;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Settings for the container targets: local image, tarball, registry, or registry + Azure app.
/// Profiles use the property names of the SDK and Visual Studio, so <c>dotnet publish -t:PublishContainer</c>
/// and Visual Studio understand them too.
/// </summary>
public sealed class ContainerProfilePage(IAnsiConsole console, ContainerCli containers, AzureSession azureSession, AzCli az)
{
    public async Task<PublishProfile> CreateAsync(ProjectInfo project, PublishTargetType destination, CancellationToken cancellationToken)
    {
        var profile = new PublishProfile("ContainerProfile");
        profile[ProfileProperties.WebPublishMethod] = "Container";

        var builder = ChooseBuilder(project, profile);
        profile[ProfileProperties.ContainerPublishMethod] = builder == ContainerBuilder.Sdk ? "NetSdk" : null;

        // A Dockerfile build always needs a local tool; the SDK only needs one for local images.
        // Checked before the other questions, so nothing is typed in vain when neither is running.
        if (builder == ContainerBuilder.Dockerfile || destination == PublishTargetType.LocalContainer)
        {
            profile[ProfileProperties.LocalRegistry] = (await ChooseToolAsync(cancellationToken)).ToString();
        }

        var repository = console.Ask("Image name", defaultValue: ContainerProfile.ToRepositoryName(project.Name), validate: ValidateRepository);
        profile[ProfileProperties.ContainerRepository] = repository;
        SetTags(profile, console.Ask("Tags", hint: "comma separated", defaultValue: "latest", validate: ValidateTags));

        switch (destination)
        {
            case PublishTargetType.LocalContainer:
                profile = profile.CopyAs($"{repository} - local image", filePath: null);
                break;

            case PublishTargetType.ContainerTarball:
                profile[ProfileProperties.ContainerArchiveOutputPath] = console.Ask("Tarball file or folder", defaultValue: "bin/container/");
                profile = profile.CopyAs($"{repository} - tarball", filePath: null);
                break;

            case PublishTargetType.ContainerRegistry:
                var registry = await ChooseRegistryAsync(profile, allowOtherRegistries: true, cancellationToken);
                profile[ProfileProperties.TargetId] = GetVisualStudioTargetId(project, builder, registry);
                profile = profile.CopyAs($"{repository} - {registry.Host}", filePath: null);
                break;

            default:
                var azureRegistry = await ChooseRegistryAsync(profile, allowOtherRegistries: false, cancellationToken);
                var appName = await ChooseAzureAppAsync(profile, destination, azureRegistry, cancellationToken);
                profile = profile.CopyAs($"{appName} - container", filePath: null);
                break;
        }

        if (destination is not (PublishTargetType.LocalContainer or PublishTargetType.ContainerTarball))
        {
            profile[ProfileProperties.PublishProvider] = ProfileProperties.ContainerRegistryProvider;

            // Registries usually feed Linux x64 hosts (Azure included), whatever this machine is; Visual Studio does the same.
            profile[ProfileProperties.RuntimeIdentifier] = builder == ContainerBuilder.Sdk ? "linux-x64" : null;
        }

        var targetFramework = CommonPrompts.ChooseTargetFramework(console, project);
        profile[ProfileProperties.LastUsedBuildConfiguration] = CommonPrompts.ChooseConfiguration(console);
        profile[ProfileProperties.LastUsedPlatform] = "Any CPU";
        profile[ProfileProperties.TargetFramework] = project.TargetFrameworks.Count > 1 ? targetFramework : null;
        return profile;
    }

    /// <summary>A registry the image is pushed to.</summary>
    /// <param name="Host">e.g. <c>contoso.azurecr.io</c> or <c>ghcr.io</c>.</param>
    /// <param name="Azure">The Azure Container Registry, or <c>null</c> for other registries.</param>
    private sealed record ChosenRegistry(string Host, AzureContainerRegistry? Azure, string? SubscriptionId);

    private static string? ValidateRepository(string repository) => ContainerProfile.IsValidRepository(repository)
        ? null
        : "Use lowercase letters and digits, with ., _ or - between them (and / between path parts), e.g. contoso/shop-api";

    private static string? ValidateTags(string tags) => SplitTags(tags).FirstOrDefault(tag => !ContainerProfile.IsValidTag(tag)) is { } invalid
        ? $"'{invalid}' isn't a valid tag: use letters, digits, _, . and -, not starting with . or -"
        : null;

    private static string[] SplitTags(string tags) => tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// One tag goes in Visual Studio's <c>PublishImageTag</c>, several in the SDK's <c>ContainerImageTags</c>
    /// (the SDK refuses both at once).
    /// </summary>
    private static void SetTags(PublishProfile profile, string answer)
    {
        var tags = SplitTags(answer);
        profile[ProfileProperties.PublishImageTag] = tags.Length == 1 ? tags[0] : null;
        profile[ProfileProperties.ContainerImageTags] = tags.Length > 1 ? string.Join(';', tags) : null;
    }

    /// <summary>Visual Studio's target ID, for the registry profiles VS creates itself (SDK builds of web projects).</summary>
    private static string? GetVisualStudioTargetId(ProjectInfo project, ContainerBuilder builder, ChosenRegistry registry)
    {
        if (builder != ContainerBuilder.Sdk || project.Kind != ProjectKind.Web)
        {
            return null;
        }

        return registry.Azure is null ? VisualStudioTargetIds.NetSdkCustomContainerRegistry : VisualStudioTargetIds.NetSdkAzureContainerRegistry;
    }

    private ContainerBuilder ChooseBuilder(ProjectInfo project, PublishProfile profile)
    {
        var dockerfile = Path.Combine(project.Directory, "Dockerfile");
        if (!File.Exists(dockerfile))
        {
            return ContainerBuilder.Sdk;
        }

        const string sdk = ".NET SDK (no Dockerfile needed)";
        var useDockerfile = console.Choose("Build the image with", [sdk, "Dockerfile (docker/podman build)"]) != sdk;

        if (!useDockerfile)
        {
            return ContainerBuilder.Sdk;
        }

        profile[ProfileProperties.PubwizContainerBuilder] = "Dockerfile";
        profile[ProfileProperties.PubwizDockerfile] = "Dockerfile";

        // Visual Studio's Dockerfiles copy paths relative to the solution folder, which is usually one level up.
        profile[ProfileProperties.PubwizDockerContext] = console.Ask("Build context", hint: "relative to the project; .. for Visual Studio style Dockerfiles", defaultValue: "..");
        return ContainerBuilder.Dockerfile;
    }

    /// <summary>
    /// The local container tool. When neither docker nor podman is running, the user can start one and check again,
    /// or go back to the targets (<see cref="BackToTargetsException"/>).
    /// </summary>
    private async Task<ContainerTool> ChooseToolAsync(CancellationToken cancellationToken)
    {
        const string checkAgain = "Check again [dim]· after starting Docker or Podman[/]";
        const string otherTarget = "Choose another target [dim]· a tarball or registry built by the .NET SDK needs neither[/]";

        while (true)
        {
            var tools = await console.Status().StartAsync("Looking for docker and podman...", _ => containers.DetectAsync(cancellationToken));
            switch (tools.Count)
            {
                case 1:
                    return tools[0];
                case > 1:
                    return console.Choose("Container tool", tools);
            }

            console.Write(Widgets.Warning("Neither docker nor podman is running."));
            console.WriteLine();
            var choice = console.Prompt(Questions.Menu<string>("[bold]What now?[/]")
                .AddChoices(checkAgain, otherTarget)
                .UseConverter(text => text));

            if (choice == otherTarget)
            {
                throw new BackToTargetsException();
            }
        }
    }

    /// <summary>
    /// Asks for the registry and writes it the way Visual Studio does: an Azure Container Registry as <c>RegistryUrl</c>
    /// (with its <c>ResourceId</c> when it's the destination), other registries as <c>ContainerRegistry</c>.
    /// </summary>
    private async Task<ChosenRegistry> ChooseRegistryAsync(PublishProfile profile, bool allowOtherRegistries, CancellationToken cancellationToken)
    {
        const string acr = "Azure Container Registry (sign in with az)";
        const string other = "Another registry (Docker Hub, GitHub, GitLab, ...)";

        var useAcr = !allowOtherRegistries || console.Choose("Registry", [acr, other]) == acr;
        if (!useAcr)
        {
            var host = console.Ask("Registry host", hint: "e.g. docker.io, ghcr.io");
            var userName = console.Ask("User name", hint: "empty = use your existing docker login", optional: true);
            profile[ProfileProperties.ContainerRegistry] = host;
            profile[ProfileProperties.UserName] = userName.Length > 0 ? userName : null;
            return new ChosenRegistry(host, Azure: null, SubscriptionId: null);
        }

        var subscription = await azureSession.EnsureReadyAsync(cancellationToken);
        var registry = await console.ChooseWithRefreshAsync("Azure Container Registry", "Loading registries...", az.ListContainerRegistriesAsync,
            r => [r.LoginServer, r.ResourceGroup],
            emptyMessage: "No Azure Container Registries were found in this subscription. Create one, then refresh.",
            cancellationToken);

        profile[ProfileProperties.RegistryUrl] = registry.LoginServer;
        if (allowOtherRegistries)
        {
            profile[ProfileProperties.ResourceId] = AzureResourceIds.ContainerRegistry(subscription.Id, registry.ResourceGroup, registry.Name);
            profile[ProfileProperties.ResourceName] = registry.Name;
            profile[ProfileProperties.ResourceGroup] = registry.ResourceGroup;
        }

        return new ChosenRegistry(registry.LoginServer, registry, subscription.Id);
    }

    /// <summary>
    /// Asks for the App Service or Container App that runs the image. Like Visual Studio, the app is the profile's
    /// <c>ResourceId</c> and the registry its <c>ContainerResourceId</c>.
    /// </summary>
    /// <returns>The chosen app's name.</returns>
    private async Task<string> ChooseAzureAppAsync(PublishProfile profile, PublishTargetType destination, ChosenRegistry registry, CancellationToken cancellationToken)
    {
        var subscription = await azureSession.EnsureReadyAsync(cancellationToken);
        string name, resourceGroup, resourceId;

        if (destination == PublishTargetType.AzureAppServiceContainer)
        {
            var app = await console.ChooseWithRefreshAsync<AzureWebApp>("App Service app", "Loading apps...",
                async token => (await az.ListWebAppsAsync(cancellationToken: token)).Where(a => a.IsContainer).ToList(),
                a => [a.Name, a.ResourceGroup],
                emptyMessage: $"No container App Service apps were found in '{subscription.Name}'. Create one, then refresh.",
                cancellationToken);
            (name, resourceGroup) = (app.Name, app.ResourceGroup);
            resourceId = AzureResourceIds.WebApp(subscription.Id, resourceGroup, name);
            profile[ProfileProperties.PubwizAzureContainerHost] = "AppService";
            profile[ProfileProperties.SiteUrlToLaunchAfterPublish] = $"https://{app.DefaultHostName}";
        }
        else
        {
            var app = await console.ChooseWithRefreshAsync("Container App", "Loading container apps...", az.ListContainerAppsAsync,
                a => [a.Name, a.ResourceGroup],
                emptyMessage: $"No Container Apps were found in '{subscription.Name}'. Create one, then refresh.",
                cancellationToken);
            (name, resourceGroup) = (app.Name, app.ResourceGroup);
            resourceId = AzureResourceIds.ContainerApp(subscription.Id, resourceGroup, name);
            profile[ProfileProperties.PubwizAzureContainerHost] = "ContainerApp";
            profile[ProfileProperties.SiteUrlToLaunchAfterPublish] = app.Fqdn is null ? null : $"https://{app.Fqdn}";
        }

        profile[ProfileProperties.LaunchSiteAfterPublish] = profile[ProfileProperties.SiteUrlToLaunchAfterPublish] is null ? null : "true";
        profile[ProfileProperties.ResourceId] = resourceId;
        profile[ProfileProperties.ResourceGroup] = resourceGroup;
        if (registry is { Azure: { } acr, SubscriptionId: { } registrySubscription })
        {
            profile[ProfileProperties.ContainerResourceId] = AzureResourceIds.ContainerRegistry(registrySubscription, acr.ResourceGroup, acr.Name);
            profile[ProfileProperties.ContainerResourceName] = acr.Name;
        }

        return name;
    }
}
