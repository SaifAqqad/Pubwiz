using Pubwiz.Core.Azure;
using Pubwiz.Core.Containers;
using Pubwiz.Core.Execution;
using Pubwiz.Core.Logging;
using Pubwiz.Core.Processes;
using Pubwiz.Core.Profiles;

namespace Pubwiz.Core.Targets.Containers;

/// <summary>
/// Gets registry credentials before building/pushing: an az token for Azure Container Registry, or the profile's
/// user name with the password. The SDK reads them from environment variables; docker/podman get a <c>login</c>.
/// With neither, the tools fall back to existing <c>docker login</c> credentials.
/// </summary>
internal sealed class RegistrySignInStep(AzCli az, ContainerCli containers, ContainerProfile container, ContainerTool? dockerfileTool) : PublishStep("Sign in to registry")
{
    // Environment variables the .NET SDK container tooling reads registry credentials from.
    private const string SdkUserNameVariable = "DOTNET_CONTAINER_REGISTRY_UNAME";
    private const string SdkPasswordVariable = "DOTNET_CONTAINER_REGISTRY_PWORD";

    public override async Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
    {
        var (userName, password) = await GetCredentialsAsync(context, cancellationToken);
        context.Log.AddSecret(password);
        if (userName is null || password is null)
        {
            context.Log.Info($"No credentials in the profile; using existing docker login credentials for {container.Registry}");
            return;
        }

        if (dockerfileTool is { } tool)
        {
            await containers.LoginAsync(tool, container.Registry!, userName, password, cancellationToken);
        }
        else
        {
            context.SetToolEnvironment(SdkUserNameVariable, userName, isSecret: false);
            context.SetToolEnvironment(SdkPasswordVariable, password, isSecret: true);
        }

        context.Log.Info($"Signed in to {container.Registry} as {userName}");
    }

    private async Task<(string? UserName, string? Password)> GetCredentialsAsync(PublishContext context, CancellationToken cancellationToken)
    {
        var profileUser = context.Request.Profile[ProfileProperties.UserName];
        if (profileUser is not null)
        {
            return (profileUser, context.Request.Password);
        }

        if (!container.IsAzureRegistry)
        {
            return (null, null);
        }

        context.ReportDetail($"az acr login --name {container.AzureRegistryName}");
        try
        {
            var token = await az.GetAcrAccessTokenAsync(container.AzureRegistryName!, cancellationToken);
            return (AzCli.AcrTokenUserName, token);
        }
        catch (AzCliException ex)
        {
            throw new PublishStepException($"Could not get a token for {container.Registry} from az ({ex.Message}). Run 'az login' and try again.");
        }
    }
}

/// <summary>Builds the image from the project's Dockerfile (and loads it into the local docker/podman).</summary>
internal sealed class DockerBuildStep(ContainerCli containers, ContainerTool tool, ContainerProfile container) : PublishStep("Build image (Dockerfile)")
{
    public override async Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
    {
        var projectFolder = context.Request.Project.Directory;
        var dockerfile = Path.GetFullPath(Path.Combine(projectFolder, container.Dockerfile));
        var buildContext = Path.GetFullPath(Path.Combine(projectFolder, container.DockerContext));

        context.Log.Info($"{ContainerCli.CommandName(tool)} build -f {dockerfile} {string.Join(' ', container.ImageNames.Select(i => $"-t {i}"))} {buildContext}");
        try
        {
            await containers.BuildAsync(tool, dockerfile, buildContext, container.ImageNames, line => LogOutput(context, line), cancellationToken);
        }
        catch (ContainerCliException ex)
        {
            throw new PublishStepException(ex.Message, ex);
        }
    }

    internal static void LogOutput(PublishContext context, ProcessOutputLine line)
    {
        if (string.IsNullOrWhiteSpace(line.Text))
        {
            return;
        }

        // docker/podman write progress to stderr, so it isn't treated as errors.
        context.Log.Write(LogSource.Output, line.Text);
        context.ReportDetail(line.Text.Trim());
    }
}

/// <summary>Pushes every tag of a Dockerfile-built image.</summary>
internal sealed class DockerPushStep(ContainerCli containers, ContainerTool tool, ContainerProfile container) : PublishStep("Push image")
{
    public override async Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var image in container.ImageNames)
            {
                await containers.PushAsync(tool, image, line => DockerBuildStep.LogOutput(context, line), cancellationToken);
            }
        }
        catch (ContainerCliException ex)
        {
            throw new PublishStepException(ex.Message, ex);
        }
    }
}

/// <summary>Saves a Dockerfile-built image to a tarball.</summary>
internal sealed class DockerSaveStep(ContainerCli containers, ContainerTool tool, ContainerProfile container, string archivePath) : PublishStep("Save image")
{
    public override async Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        try
        {
            await containers.SaveAsync(tool, container.ImageNames, archivePath, line => DockerBuildStep.LogOutput(context, line), cancellationToken);
        }
        catch (ContainerCliException ex)
        {
            throw new PublishStepException(ex.Message, ex);
        }

        context.PackagePath = archivePath;
    }
}

/// <summary>Points an App Service or Container App at the pushed image.</summary>
internal sealed class AzureImageUpdateStep(AzCli az, ContainerProfile container, string resourceGroup, string appName) : PublishStep("Update Azure app")
{
    public override async Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
    {
        var image = container.ImageNames[0];
        context.ReportDetail($"{appName} → {image}");
        try
        {
            if (container.AzureHost == "AppService")
            {
                await az.SetWebAppImageAsync(resourceGroup, appName, image, cancellationToken);
            }
            else
            {
                await az.UpdateContainerAppImageAsync(resourceGroup, appName, image, cancellationToken);
            }
        }
        catch (AzCliException ex)
        {
            throw new PublishStepException($"Could not update {appName}: {ex.Message}", ex);
        }

        context.Log.Info($"{appName} now runs {image}");
    }
}
