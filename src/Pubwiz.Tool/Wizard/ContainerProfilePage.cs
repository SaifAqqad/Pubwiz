using Pubwiz.Core.Azure;
using Pubwiz.Core.Containers;
using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets;
using Pubwiz.Core.Targets.Containers;
using Pubwiz.Tool.Azure;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Settings for the container targets: local image, tarball, registry, or registry + Azure app.
/// Profiles use the SDK's container properties, so <c>dotnet publish -t:PublishContainer</c> understands them too.
/// </summary>
public sealed class ContainerProfilePage(IAnsiConsole console, ContainerCli containers, AzureSession azureSession, AzCli az)
{
    public async Task<PublishProfile> CreateAsync(ProjectInfo project, PublishTargetType destination, CancellationToken cancellationToken)
    {
        var profile = new PublishProfile("ContainerProfile");
        profile[ProfileProperties.WebPublishMethod] = "Container";

        var builder = ChooseBuilder(project, profile);
        var repository = console.Prompt(new TextPrompt<string>("Image name:").DefaultValue(project.Name.ToLowerInvariant()));
        var tags = console.Prompt(new TextPrompt<string>("Tags [dim](comma separated)[/]:").DefaultValue("latest"));
        profile[ProfileProperties.ContainerRepository] = repository;
        profile[ProfileProperties.ContainerImageTags] = string.Join(';', tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        // A Dockerfile build always needs a local tool; the SDK only needs one for local images.
        if (builder == ContainerBuilder.Dockerfile || destination == PublishTargetType.LocalContainer)
        {
            profile[ProfileProperties.LocalRegistry] = (await ChooseToolAsync(cancellationToken)).ToString();
        }

        switch (destination)
        {
            case PublishTargetType.LocalContainer:
                profile = profile.CopyAs($"{repository} - local image", filePath: null);
                break;

            case PublishTargetType.ContainerTarball:
                profile[ProfileProperties.ContainerArchiveOutputPath] = console.Prompt(new TextPrompt<string>("Tarball file or folder:").DefaultValue("bin/container/"));
                profile = profile.CopyAs($"{repository} - tarball", filePath: null);
                break;

            case PublishTargetType.ContainerRegistry:
                var registry = await ChooseRegistryAsync(profile, allowOtherRegistries: true, cancellationToken);
                profile = profile.CopyAs($"{repository} - {registry}", filePath: null);
                break;

            default:
                await ChooseRegistryAsync(profile, allowOtherRegistries: false, cancellationToken);
                var appName = await ChooseAzureAppAsync(profile, destination, cancellationToken);
                profile = profile.CopyAs($"{appName} - container", filePath: null);
                break;
        }

        var targetFramework = CommonPrompts.ChooseTargetFramework(console, project);
        profile[ProfileProperties.LastUsedBuildConfiguration] = CommonPrompts.ChooseConfiguration(console);
        profile[ProfileProperties.TargetFramework] = project.TargetFrameworks.Count > 1 ? targetFramework : null;
        return profile;
    }

    private ContainerBuilder ChooseBuilder(ProjectInfo project, PublishProfile profile)
    {
        var dockerfile = Path.Combine(project.Directory, "Dockerfile");
        if (!File.Exists(dockerfile))
        {
            return ContainerBuilder.Sdk;
        }

        var useDockerfile = console.Prompt(new SelectionPrompt<string>()
            .Title("This project has a Dockerfile. How should the image be built?")
            .HighlightStyle(new Style(Widgets.PrimaryColor))
            .AddChoices(".NET SDK (no Dockerfile needed)", "Dockerfile (docker/podman build)")) != ".NET SDK (no Dockerfile needed)";

        if (!useDockerfile)
        {
            return ContainerBuilder.Sdk;
        }

        profile[ProfileProperties.PubwizContainerBuilder] = "Dockerfile";
        profile[ProfileProperties.PubwizDockerfile] = "Dockerfile";

        // Visual Studio's Dockerfiles copy paths relative to the solution folder, which is usually one level up.
        profile[ProfileProperties.PubwizDockerContext] = console.Prompt(new TextPrompt<string>("Build context, relative to the project [dim](.. for Visual Studio style Dockerfiles)[/]:").DefaultValue(".."));
        return ContainerBuilder.Dockerfile;
    }

    private async Task<ContainerTool> ChooseToolAsync(CancellationToken cancellationToken)
    {
        var tools = await console.Status().StartAsync("Looking for docker and podman...", _ => containers.DetectAsync(cancellationToken));
        return tools.Count switch
        {
            0 => throw new UserErrorException("Neither docker nor podman is running. Start one of them, or publish the image to a tarball or registry with the .NET SDK instead."),
            1 => tools[0],
            _ => console.Prompt(new SelectionPrompt<ContainerTool>().Title("Container tool:").HighlightStyle(new Style(Widgets.PrimaryColor)).AddChoices(tools))
        };
    }

    /// <returns>The registry host written to the profile.</returns>
    private async Task<string> ChooseRegistryAsync(PublishProfile profile, bool allowOtherRegistries, CancellationToken cancellationToken)
    {
        const string acr = "Azure Container Registry (sign in with az)";
        const string other = "Another registry (Docker Hub, GitHub, GitLab, ...)";

        var useAcr = !allowOtherRegistries || console.Prompt(new SelectionPrompt<string>()
            .Title("Registry:")
            .HighlightStyle(new Style(Widgets.PrimaryColor))
            .AddChoices(acr, other)) == acr;

        string registry;
        if (useAcr)
        {
            await azureSession.EnsureReadyAsync(cancellationToken);
            var registries = await console.Status().StartAsync("Loading registries...", _ => az.ListContainerRegistriesAsync(cancellationToken));
            if (registries.Count == 0)
            {
                throw new UserErrorException("No Azure Container Registries were found in this subscription.");
            }

            registry = console.Prompt(new SelectionPrompt<AzureContainerRegistry>()
                .Title("Azure Container Registry:")
                .HighlightStyle(new Style(Widgets.PrimaryColor))
                .AddChoices(registries)
                .UseConverter(r => $"{r.LoginServer.EscapeMarkup()} [dim]{r.ResourceGroup.EscapeMarkup()}[/]")).LoginServer;
        }
        else
        {
            registry = console.Prompt(new TextPrompt<string>("Registry host [dim](e.g. docker.io, ghcr.io)[/]:"));
            var userName = console.Prompt(new TextPrompt<string>("User name [dim](empty = use your existing docker login)[/]:").AllowEmpty());
            profile[ProfileProperties.UserName] = userName.Length > 0 ? userName : null;
        }

        profile[ProfileProperties.ContainerRegistry] = registry;
        return registry;
    }

    /// <returns>The chosen app's name.</returns>
    private async Task<string> ChooseAzureAppAsync(PublishProfile profile, PublishTargetType destination, CancellationToken cancellationToken)
    {
        var subscription = await azureSession.EnsureReadyAsync(cancellationToken);
        string name, resourceGroup;

        if (destination == PublishTargetType.AzureAppServiceContainer)
        {
            var apps = (await console.Status().StartAsync("Loading apps...", _ => az.ListWebAppsAsync(cancellationToken))).Where(a => a.IsContainer).ToList();
            if (apps.Count == 0)
            {
                throw new UserErrorException($"No container App Service apps were found in '{subscription.Name}'.");
            }

            var app = console.Prompt(new SelectionPrompt<AzureWebApp>().Title("App Service app:").HighlightStyle(new Style(Widgets.PrimaryColor))
                .AddChoices(apps).UseConverter(a => $"{a.Name.EscapeMarkup()} [dim]{a.ResourceGroup.EscapeMarkup()}[/]"));
            (name, resourceGroup) = (app.Name, app.ResourceGroup);
            profile[ProfileProperties.PubwizAzureContainerHost] = "AppService";
            profile[ProfileProperties.SiteUrlToLaunchAfterPublish] = $"https://{app.DefaultHostName}";
        }
        else
        {
            var apps = await console.Status().StartAsync("Loading container apps...", _ => az.ListContainerAppsAsync(cancellationToken));
            if (apps.Count == 0)
            {
                throw new UserErrorException($"No Container Apps were found in '{subscription.Name}'.");
            }

            var app = console.Prompt(new SelectionPrompt<AzureContainerApp>().Title("Container App:").HighlightStyle(new Style(Widgets.PrimaryColor))
                .AddChoices(apps).UseConverter(a => $"{a.Name.EscapeMarkup()} [dim]{a.ResourceGroup.EscapeMarkup()}[/]"));
            (name, resourceGroup) = (app.Name, app.ResourceGroup);
            profile[ProfileProperties.PubwizAzureContainerHost] = "ContainerApp";
            profile[ProfileProperties.SiteUrlToLaunchAfterPublish] = app.Fqdn is null ? null : $"https://{app.Fqdn}";
        }

        profile[ProfileProperties.PubwizAzureSubscriptionId] = subscription.Id;
        profile[ProfileProperties.PubwizAzureResourceGroup] = resourceGroup;
        profile[ProfileProperties.PubwizAzureAppName] = name;
        return name;
    }
}
