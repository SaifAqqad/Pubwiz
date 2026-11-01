using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets;
using Pubwiz.Core.Targets.Azure;
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

        // A Dockerfile build always needs a local tool; the SDK only needs one for local images.
        // Checked before the other questions, so nothing is typed in vain when neither is running.
        if (builder == ContainerBuilder.Dockerfile || destination == PublishTargetType.LocalContainer)
        {
            profile[ProfileProperties.LocalRegistry] = (await ChooseToolAsync(cancellationToken)).ToString();
        }

        var repository = console.Ask("Image name", defaultValue: project.Name.ToLowerInvariant());
        var tags = console.Ask("Tags", hint: "comma separated", defaultValue: "latest");
        profile[ProfileProperties.ContainerRepository] = repository;
        profile[ProfileProperties.ContainerImageTags] = string.Join(';', tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

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

    /// <returns>The registry host written to the profile.</returns>
    private async Task<string> ChooseRegistryAsync(PublishProfile profile, bool allowOtherRegistries, CancellationToken cancellationToken)
    {
        const string acr = "Azure Container Registry (sign in with az)";
        const string other = "Another registry (Docker Hub, GitHub, GitLab, ...)";

        var useAcr = !allowOtherRegistries || console.Choose("Registry", [acr, other]) == acr;

        string registry;
        if (useAcr)
        {
            await azureSession.EnsureReadyAsync(cancellationToken);
            registry = (await console.ChooseWithRefreshAsync("Azure Container Registry", "Loading registries...", az.ListContainerRegistriesAsync,
                r => [r.LoginServer, r.ResourceGroup],
                emptyMessage: "No Azure Container Registries were found in this subscription. Create one, then refresh.",
                cancellationToken)).LoginServer;
        }
        else
        {
            registry = console.Ask("Registry host", hint: "e.g. docker.io, ghcr.io");
            var userName = console.Ask("User name", hint: "empty = use your existing docker login", optional: true);
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
            var app = await console.ChooseWithRefreshAsync<AzureWebApp>("App Service app", "Loading apps...",
                async token => (await az.ListWebAppsAsync(cancellationToken: token)).Where(a => a.IsContainer).ToList(),
                a => [a.Name, a.ResourceGroup],
                emptyMessage: $"No container App Service apps were found in '{subscription.Name}'. Create one, then refresh.",
                cancellationToken);
            (name, resourceGroup) = (app.Name, app.ResourceGroup);
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
            profile[ProfileProperties.PubwizAzureContainerHost] = "ContainerApp";
            profile[ProfileProperties.SiteUrlToLaunchAfterPublish] = app.Fqdn is null ? null : $"https://{app.Fqdn}";
        }

        profile[ProfileProperties.PubwizAzureSubscriptionId] = subscription.Id;
        profile[ProfileProperties.PubwizAzureResourceGroup] = resourceGroup;
        profile[ProfileProperties.PubwizAzureAppName] = name;
        return name;
    }
}
