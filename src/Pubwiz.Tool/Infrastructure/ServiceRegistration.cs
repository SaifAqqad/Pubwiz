using Microsoft.Extensions.DependencyInjection;
using Pubwiz.Core;
using Pubwiz.Core.Azure;
using Pubwiz.Core.Azure.Kudu;
using Pubwiz.Core.Discovery;
using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Execution;
using Pubwiz.Core.Processes;
using Pubwiz.Core.Secrets;
using Pubwiz.Core.Targets.Azure;
using Pubwiz.Core.Targets.FileTransfer;
using Pubwiz.Core.Targets.Folder;
using Pubwiz.Tool.Azure;
using Pubwiz.Tool.Credentials;
using Pubwiz.Tool.Rendering;
using Pubwiz.Tool.Wizard;

namespace Pubwiz.Tool.Infrastructure;

/// <summary>
/// The tool's dependency injection setup.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddPubwiz(this IServiceCollection services)
    {
        services.AddSingleton(AnsiConsole.Console);
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton(CreateDotnetCli);
        services.AddSingleton(sp => SecretStoreFactory.Create(sp.GetRequiredService<IProcessRunner>()));

        // Core
        services.AddSingleton<ProjectInspector>();
        services.AddSingleton<WorkspaceLoader>();
        services.AddSingleton<PublishRunner>();
        services.AddSingleton<AzCli>();
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromMinutes(30) });
        services.AddSingleton<KuduClient>();
        services.AddSingleton<KuduCredentialsProvider>();

        // Publish targets: the runner picks the first one that can publish a profile
        services.AddSingleton<IPublishTarget, FolderTarget>();
        services.AddSingleton<IPublishTarget>(sp => new FileTransferTarget(sp.GetRequiredService<DotnetCli>(), sftp: false));
        services.AddSingleton<IPublishTarget>(sp => new FileTransferTarget(sp.GetRequiredService<DotnetCli>(), sftp: true));
        services.AddSingleton<IPublishTarget, AzureWebJobTarget>();
        services.AddSingleton<IPublishTarget, AzureAppServiceTarget>();

        // Wizard
        services.AddSingleton<PublishWizard>();
        services.AddSingleton<WorkspacePage>();
        services.AddSingleton<ProfilePage>();
        services.AddSingleton<ImportProfilePage>();
        services.AddSingleton<CreateProfilePage>();
        services.AddSingleton<FolderProfilePage>();
        services.AddSingleton<WebDeployProfilePage>();
        services.AddSingleton<FileTransferProfilePage>();
        services.AddSingleton<AzureProfilePage>();
        services.AddSingleton<AzureSession>();
        services.AddSingleton<PasswordProvider>();

        // Publishing UI
        services.AddSingleton<PublishDashboard>();
        services.AddSingleton<ResultsView>();

        return services;
    }

    private static DotnetCli CreateDotnetCli(IServiceProvider services)
    {
        var path = DotnetLocator.Find() ?? throw new PrerequisiteMissingException("dotnet", "Install the .NET SDK or set DOTNET_ROOT.");
        return new DotnetCli(services.GetRequiredService<IProcessRunner>(), path);
    }
}
