using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets.FileTransfer;
using Pubwiz.Tool.Infrastructure;

namespace Pubwiz.Tool.Wizard;

/// <summary>Values to suggest in the FTP/SFTP prompts, e.g. taken from a Web Deploy profile for the same server.</summary>
public sealed record FileTransferDefaults(string? Host, string? UserName, string? SiteUrl);

/// <summary>
/// Settings for the FTP/FTPS and SFTP targets. Passwords are asked for when publishing (and can be saved in the keychain).
/// </summary>
public sealed class FileTransferProfilePage(IAnsiConsole console)
{
    /// <param name="project">The project being published.</param>
    /// <param name="sftp"><c>true</c> for SFTP, <c>false</c> for FTP/FTPS.</param>
    /// <param name="defaults">Suggested answers, or <c>null</c>.</param>
    /// <returns>An unsaved profile.</returns>
    public PublishProfile Create(ProjectInfo project, bool sftp, FileTransferDefaults? defaults = null)
    {
        var host = console.Ask("Server", hint: defaults?.Host is null ? "e.g. ftp.contoso.com" : null, defaultValue: defaults?.Host);
        var port = console.AskNumber("Port", sftp ? 22 : 21);
        var remoteFolder = console.Ask("Folder on the server", hint: "e.g. /site/wwwroot", defaultValue: "/");
        var userName = console.Ask("User name", defaultValue: defaults?.UserName);

        var profile = new PublishProfile(sftp ? "SftpProfile" : "FtpProfile");
        var scheme = sftp ? "sftp" : "ftp";

        if (sftp)
        {
            profile[ProfileProperties.WebPublishMethod] = "Custom";
            profile[ProfileProperties.PublishProvider] = ProfileProperties.SftpProvider;
        }
        else
        {
            var security = console.Choose("Encryption", [FtpSecurity.Explicit, FtpSecurity.Auto, FtpSecurity.Implicit, FtpSecurity.None], DescribeSecurity);

            if (security == FtpSecurity.Explicit)
            {
                scheme = "ftps";
            }

            profile[ProfileProperties.WebPublishMethod] = "FTP";
            profile[ProfileProperties.TargetId] = VisualStudioTargetIds.Ftp;
            profile[ProfileProperties.PubwizFtpSecurity] = security.ToString();
            profile[ProfileProperties.FtpPassiveMode] = console.AskYesNo("Use passive mode?", defaultValue: true) ? "true" : "false";
            profile[ProfileProperties.AllowUntrustedCertificate] = security != FtpSecurity.None && console.AskYesNo("Allow an untrusted (self-signed) certificate?", defaultValue: false) ? "true" : null;
        }

        profile[ProfileProperties.PublishUrl] = $"{scheme}://{host}:{port}/{remoteFolder.Trim('/')}";
        profile[ProfileProperties.UserName] = userName;

        if (sftp)
        {
            var keyFile = console.Ask("Private key file", hint: "empty = use a password", optional: true);
            profile[ProfileProperties.PubwizSshPrivateKeyFile] = keyFile.Length > 0 ? UserPath.ToFullPath(keyFile) : null;
        }

        profile[ProfileProperties.DeleteExistingFiles] = console.AskYesNo("Delete files on the server that aren't part of the app?", defaultValue: false) ? "true" : "false";
        profile[ProfileProperties.PubwizUseAppOffline] = console.AskYesNo("Take the app offline while uploading?", defaultValue: false, hint: "IIS, app_offline.htm") ? "true" : null;

        if (sftp)
        {
            // Run in the server's own shell (bash on Linux, cmd or PowerShell on Windows).
            var appName = project.Name.ToLowerInvariant();
            profile[ProfileProperties.PubwizSshPreDeployCommand] = AskCommand("Command to run on the server before uploading", $"e.g. sudo systemctl stop {appName}");
            profile[ProfileProperties.PubwizSshPostDeployCommand] = AskCommand("Command to run on the server after uploading", $"e.g. sudo systemctl restart {appName}");
        }

        var siteUrl = console.Ask("Site URL", hint: "optional, shown after publishing", defaultValue: defaults?.SiteUrl, optional: true);
        profile[ProfileProperties.SiteUrlToLaunchAfterPublish] = siteUrl.Length > 0 ? siteUrl : null;
        profile[ProfileProperties.LaunchSiteAfterPublish] = siteUrl.Length > 0 ? "true" : null;
        profile[ProfileProperties.ExcludeAppData] = project.Kind is ProjectKind.Web ? "false" : null;

        var targetFramework = CommonPrompts.ChooseTargetFramework(console, project);
        profile[ProfileProperties.LastUsedBuildConfiguration] = CommonPrompts.ChooseConfiguration(console);
        profile[ProfileProperties.LastUsedPlatform] = "Any CPU";
        profile[ProfileProperties.TargetFramework] = project.TargetFrameworks.Count > 1 ? targetFramework : null;
        return profile;
    }

    /// <returns>The command, or <c>null</c> when left empty.</returns>
    private string? AskCommand(string question, string example)
    {
        var command = console.Ask(question, hint: $"optional, over SSH; {example}", optional: true);
        return command.Length > 0 ? command : null;
    }

    private static string DescribeSecurity(FtpSecurity security) => security switch
    {
        FtpSecurity.Explicit => "FTPS, explicit TLS [dim](recommended; Azure App Service)[/]",
        FtpSecurity.Auto => "TLS if the server supports it",
        FtpSecurity.Implicit => "FTPS, implicit TLS [dim](port 990)[/]",
        _ => "None [dim](plain FTP, unencrypted)[/]"
    };
}
