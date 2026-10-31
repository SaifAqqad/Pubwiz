using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.WebDeploy;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Settings for the IIS Web Deploy target. The password isn't stored in the profile:
/// it's asked for when publishing and can be saved in the OS keychain. Without a user name,
/// msdeploy signs in as the current Windows account (NTLM).
/// </summary>
public sealed class WebDeployProfilePage(IAnsiConsole console)
{
    /// <returns>An unsaved profile with the properties Visual Studio writes for Web Deploy.</returns>
    public PublishProfile Create(ProjectInfo project)
    {
        var server = console.Prompt(new TextPrompt<string>("Server [dim](e.g. iis.contoso.com or https://iis.contoso.com:8172/msdeploy.axd)[/]:"));
        var site = console.Prompt(new TextPrompt<string>("IIS site name [dim](e.g. Default Web Site or Default Web Site/api)[/]:"));
        var userName = console.Prompt(new TextPrompt<string>("User name [dim](empty = current Windows account)[/]:").AllowEmpty());

        var endpoint = WebDeployEndpoint.Create(server, site);
        console.MarkupLine($"[dim]Deploying through {endpoint.ServiceUrl.ToString().EscapeMarkup()}[/]");

        var allowUntrusted = console.Confirm("Allow an untrusted (self-signed) server certificate?", defaultValue: !endpoint.IsAzure);
        var siteUrl = console.Prompt(new TextPrompt<string>("Site URL to open after publishing [dim](optional)[/]:").AllowEmpty());
        var removeExtraFiles = console.Confirm("Delete files on the server that aren't part of the app?", defaultValue: false);
        var takeOffline = console.Confirm("Take the app offline while copying? [dim](avoids \"file in use\" errors)[/]", defaultValue: true);

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
        profile[ProfileProperties.EnableMSDeployAppOffline] = takeOffline ? "true" : null;
        profile[ProfileProperties.UserName] = userName.Length > 0 ? userName : null;
        profile[ProfileProperties.AuthType] = userName.Length > 0 ? "Basic" : "NTLM";
        profile[ProfileProperties.AllowUntrustedCertificate] = allowUntrusted ? "true" : "false";
        profile[ProfileProperties.TargetFramework] = project.TargetFrameworks.Count > 1 ? targetFramework : null;
        return profile;
    }
}
