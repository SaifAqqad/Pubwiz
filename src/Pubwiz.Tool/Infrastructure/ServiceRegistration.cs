using Microsoft.Extensions.DependencyInjection;
using Pubwiz.Core;
using Pubwiz.Core.Discovery;
using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Execution;
using Pubwiz.Core.Processes;
using Pubwiz.Core.Secrets;
using Pubwiz.Core.Targets.Folder;
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

        // Publish targets: the runner picks the first one that can publish a profile
        services.AddSingleton<IPublishTarget, FolderTarget>();

        // Wizard
        services.AddSingleton<PublishWizard>();
        services.AddSingleton<WorkspacePage>();
        services.AddSingleton<ProfilePage>();
        services.AddSingleton<ImportProfilePage>();
        services.AddSingleton<CreateProfilePage>();
        services.AddSingleton<FolderProfilePage>();
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
