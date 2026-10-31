using Microsoft.Extensions.DependencyInjection;
using Pubwiz.Core;
using Pubwiz.Core.Discovery;
using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Processes;
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

        // Core
        services.AddSingleton<ProjectInspector>();
        services.AddSingleton<WorkspaceLoader>();

        // Wizard
        services.AddSingleton<PublishWizard>();
        services.AddSingleton<WorkspacePage>();
        services.AddSingleton<ProfilePage>();
        services.AddSingleton<ImportProfilePage>();
        services.AddSingleton<CreateProfilePage>();
        services.AddSingleton<FolderProfilePage>();

        return services;
    }

    private static DotnetCli CreateDotnetCli(IServiceProvider services)
    {
        var path = DotnetLocator.Find() ?? throw new PrerequisiteMissingException("dotnet", "Install the .NET SDK or set DOTNET_ROOT.");
        return new DotnetCli(services.GetRequiredService<IProcessRunner>(), path);
    }
}
