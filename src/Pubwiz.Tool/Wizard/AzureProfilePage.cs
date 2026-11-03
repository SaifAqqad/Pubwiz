using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets.Azure;
using Pubwiz.Core.Targets.Azure.StaticWebApps;
using Pubwiz.Tool.Azure;

namespace Pubwiz.Tool.Wizard;

/// <summary>What kind of Azure app a profile deploys to.</summary>
public enum AzureAppKind
{
    /// <summary>An App Service web app.</summary>
    WebApp,

    /// <summary>A Function App.</summary>
    FunctionApp,

    /// <summary>A WebJob inside an App Service app.</summary>
    WebJob
}

/// <summary>
/// Creates Azure App Service, Function App, WebJob and Static Web Apps profiles by picking an existing app from the subscription.
/// Profiles sign in with az by default, so no password is stored anywhere.
/// </summary>
public sealed class AzureProfilePage(IAnsiConsole console, AzureSession session, AzCli az)
{
    public async Task<PublishProfile> CreateAsync(ProjectInfo project, AzureAppKind kind, CancellationToken cancellationToken)
    {
        var subscription = await session.EnsureReadyAsync(cancellationToken);
        var app = await ChooseAppAsync(kind, subscription, cancellationToken);

        var profile = new PublishProfile(kind == AzureAppKind.WebJob ? $"{app.Name} - WebJob" : $"{app.Name} - Zip Deploy");
        profile[ProfileProperties.WebPublishMethod] = "ZipDeploy";
        profile[ProfileProperties.PublishProvider] = ProfileProperties.AzureWebSiteProvider;
        profile[ProfileProperties.PublishUrl] = app.ScmUrl.AbsoluteUri;
        profile[ProfileProperties.SiteUrlToLaunchAfterPublish] = $"https://{app.DefaultHostName}";
        profile[ProfileProperties.PubwizAzureAuthentication] = AzureProfile.AzCliAuthentication;
        profile[ProfileProperties.PubwizAzureSubscriptionId] = subscription.Id;
        profile[ProfileProperties.PubwizAzureResourceGroup] = app.ResourceGroup;
        profile[ProfileProperties.PubwizAzureAppName] = app.Name;
        profile[ProfileProperties.PubwizAzureLinux] = app.IsLinux ? "true" : "false";

        if (kind == AzureAppKind.WebJob)
        {
            AskWebJobSettings(project, profile);
        }

        var targetFramework = CommonPrompts.ChooseTargetFramework(console, project);
        profile[ProfileProperties.LastUsedBuildConfiguration] = CommonPrompts.ChooseConfiguration(console);
        profile[ProfileProperties.LastUsedPlatform] = "Any CPU";
        profile[ProfileProperties.TargetFramework] = project.TargetFrameworks.Count > 1 ? targetFramework : null;
        return profile;
    }

    /// <summary>A Static Web Apps profile: the deployment token comes from az when publishing.</summary>
    public async Task<PublishProfile> CreateStaticWebAppAsync(ProjectInfo project, CancellationToken cancellationToken)
    {
        var subscription = await session.EnsureReadyAsync(cancellationToken);
        var app = await console.ChooseWithRefreshAsync("Static Web App", "Loading Static Web Apps...", az.ListStaticWebAppsAsync,
            a => [a.Name, a.ResourceGroup, a.Sku ?? string.Empty],
            emptyMessage: $"No Static Web Apps were found in '{subscription.Name}'. Create one in the Azure portal or with az staticwebapp create, then refresh.",
            cancellationToken);

        var profile = new PublishProfile($"{app.Name} - Static Web App");
        profile[ProfileProperties.WebPublishMethod] = "Custom";
        profile[ProfileProperties.PublishProvider] = ProfileProperties.AzureStaticWebAppProvider;
        profile[ProfileProperties.PubwizAzureSubscriptionId] = subscription.Id;
        profile[ProfileProperties.PubwizAzureResourceGroup] = app.ResourceGroup;
        profile[ProfileProperties.PubwizAzureAppName] = app.Name;
        profile[ProfileProperties.SiteUrlToLaunchAfterPublish] = app.DefaultHostname is null ? null : $"https://{app.DefaultHostname}";

        var environment = console.Ask("Environment", hint: "empty = production; a name deploys a preview environment, e.g. staging", optional: true);
        profile[ProfileProperties.PubwizStaticWebAppEnvironment] = environment.Length > 0 ? environment : null;

        var targetFramework = CommonPrompts.ChooseTargetFramework(console, project);
        profile[ProfileProperties.LastUsedBuildConfiguration] = CommonPrompts.ChooseConfiguration(console);
        profile[ProfileProperties.TargetFramework] = project.TargetFrameworks.Count > 1 ? targetFramework : null;
        return profile;
    }

    private async Task<AzureWebApp> ChooseAppAsync(AzureAppKind kind, AzureSubscription subscription, CancellationToken cancellationToken)
    {
        var question = kind switch
        {
            AzureAppKind.WebJob => "App Service app to host the WebJob",
            AzureAppKind.FunctionApp => "Function App",
            _ => "App Service app"
        };

        var what = kind == AzureAppKind.FunctionApp ? "Function Apps" : "App Service apps";
        return await console.ChooseWithRefreshAsync(question, "Loading apps...", LoadMatchingAsync,
            a => [a.Name, a.ResourceGroup, a.IsLinux ? "Linux" : "Windows", a.State ?? string.Empty],
            emptyMessage: $"No {what} were found in '{subscription.Name}'. Create one in the Azure portal or with az, then refresh.",
            cancellationToken);

        async Task<IReadOnlyList<AzureWebApp>> LoadMatchingAsync(CancellationToken token)
        {
            // Container apps run an image, so a zip deploy wouldn't change what they run.
            var apps = await az.ListWebAppsAsync(cancellationToken: token);
            return apps.Where(a => !a.IsContainer && a.IsFunctionApp == (kind == AzureAppKind.FunctionApp)).ToList();
        }
    }

    private void AskWebJobSettings(ProjectInfo project, PublishProfile profile)
    {
        // Visual Studio names the WebJob exactly like the project (same case).
        profile[ProfileProperties.WebJobName] = console.Ask("WebJob name", defaultValue: project.Name);

        // Workers run all the time; console apps usually run on demand or on a schedule.
        string[] types = project.Kind == ProjectKind.Worker ? ["Continuous", "Triggered"] : ["Triggered", "Continuous"];
        var continuous = console.Choose("WebJob type", types) == "Continuous";

        profile[ProfileProperties.WebJobType] = continuous ? "Continuous" : "Triggered";
        if (!continuous)
        {
            var schedule = console.Ask("CRON schedule", hint: "optional, e.g. 0 */15 * * * *; empty = run on demand", optional: true);
            profile[ProfileProperties.PubwizWebJobSchedule] = schedule.Length > 0 ? schedule : null;
        }
    }
}
