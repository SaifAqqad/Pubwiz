using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets.ServerLogins;
using Pubwiz.Core.Targets.WebDeploy;
using Pubwiz.WebDeploy;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Settings for the IIS Web Deploy target. The password isn't stored in the profile:
/// it's asked for when publishing and can be saved in the OS keychain. Without a user name,
/// msdeploy signs in as the current Windows account (NTLM); that's only possible where msdeploy.exe is installed,
/// since Pubwiz's own client (macOS, Linux, Windows without Web Deploy) signs in with a user name and password.
/// </summary>
public sealed class WebDeployProfilePage(IAnsiConsole console, MsDeployCli msdeploy, ServerCredentialStore servers)
{
    /// <returns>An unsaved profile with the properties Visual Studio writes for Web Deploy.</returns>
    public PublishProfile Create(ProjectInfo project)
    {
        var server = console.Ask("Server", hint: "e.g. iis.contoso.com or https://iis.contoso.com:8172/msdeploy.axd");
        var site = console.Ask("IIS site", hint: "e.g. Default Web Site or Default Web Site/api");
        var endpoint = WebDeployEndpoint.Create(server, site);
        var userName = AskUserName(endpoint.ServiceUrl.Host);
        console.MarkupLine($"[dim]Deploying through {endpoint.ServiceUrl.ToString().EscapeMarkup()}[/]");

        var allowUntrusted = console.AskYesNo("Allow an untrusted (self-signed) server certificate?", defaultValue: !endpoint.IsAzure);
        var siteUrl = console.Ask("Site URL", hint: "optional, shown after publishing", optional: true);
        var removeExtraFiles = console.AskYesNo("Delete files on the server that aren't part of the app?", defaultValue: false);

        var targetFramework = CommonPrompts.ChooseTargetFramework(console, project);
        var configuration = CommonPrompts.ChooseConfiguration(console);

        var profile = new PublishProfile("WebDeployProfile");
        profile[ProfileProperties.WebPublishMethod] = "MSDeploy";
        profile[ProfileProperties.LastUsedBuildConfiguration] = configuration;
        profile[ProfileProperties.LastUsedPlatform] = "Any CPU";
        profile[ProfileProperties.SiteUrlToLaunchAfterPublish] = siteUrl.Length > 0 ? siteUrl : null;
        profile[ProfileProperties.LaunchSiteAfterPublish] = siteUrl.Length > 0 ? "true" : null;
        profile[ProfileProperties.ExcludeAppData] = "false";
        profile[ProfileProperties.MSDeployServiceURL] = server;
        profile[ProfileProperties.DeployIisAppPath] = site;
        profile[ProfileProperties.SkipExtraFilesOnServer] = removeExtraFiles ? "false" : "true";
        profile[ProfileProperties.MSDeployPublishMethod] = "WMSVC";
        profile[ProfileProperties.EnableMSDeployBackup] = "false";
        // Pubwiz always takes the app offline (unless --no-app-offline); this makes Visual Studio do the same.
        profile[ProfileProperties.EnableMSDeployAppOffline] = "true";
        profile[ProfileProperties.UserName] = userName.Length > 0 ? userName : null;
        profile[ProfileProperties.AuthType] = userName.Length > 0 ? "Basic" : "NTLM";
        profile[ProfileProperties.AllowUntrustedCertificate] = allowUntrusted ? "true" : "false";
        profile[ProfileProperties.TargetFramework] = project.TargetFrameworks.Count > 1 ? targetFramework : null;

        // Visual Studio opens Azure Web Deploy profiles from their URL; others are "Web Server (IIS)" profiles.
        profile[ProfileProperties.TargetId] = endpoint.IsAzure ? null : VisualStudioTargetIds.IisWebDeploy;
        return profile;
    }

    /// <summary>The user to sign in as; empty for the current Windows account, or for the server's saved login.</summary>
    private string AskUserName(string host)
    {
        if (msdeploy.IsInstalled)
        {
            return console.Ask("User name", hint: "empty = current Windows account", optional: true);
        }

        // Pubwiz's own client can't sign in as the current Windows account, so a user name is needed,
        // unless one is saved for this server (dotnet pubwiz servers set).
        return servers.Find(host) is { } saved
            ? console.Ask("User name", hint: $"empty = the login saved for {host} ({saved.UserName})", optional: true)
            : console.Ask("User name", hint: "an IIS Manager or Windows user on the server");
    }
}
