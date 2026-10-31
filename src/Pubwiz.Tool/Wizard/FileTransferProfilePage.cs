using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Transfer;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Settings for the FTP/FTPS and SFTP targets. Passwords are asked for when publishing (and can be saved in the keychain).
/// </summary>
public sealed class FileTransferProfilePage(IAnsiConsole console)
{
    /// <param name="project">The project being published.</param>
    /// <param name="sftp"><c>true</c> for SFTP, <c>false</c> for FTP/FTPS.</param>
    /// <returns>An unsaved profile.</returns>
    public PublishProfile Create(ProjectInfo project, bool sftp)
    {
        var host = console.Prompt(new TextPrompt<string>("Server:"));
        var port = console.Prompt(new TextPrompt<int>("Port:").DefaultValue(sftp ? 22 : 21));
        var remoteFolder = console.Prompt(new TextPrompt<string>("Folder on the server [dim](e.g. /site/wwwroot)[/]:").DefaultValue("/"));
        var userName = console.Prompt(new TextPrompt<string>("User name:"));

        var profile = new PublishProfile(sftp ? "SftpProfile" : "FtpProfile");
        var scheme = sftp ? "sftp" : "ftp";

        if (sftp)
        {
            profile[ProfileProperties.WebPublishMethod] = "Custom";
            profile[ProfileProperties.PublishProvider] = ProfileProperties.SftpProvider;
        }
        else
        {
            var security = console.Prompt(new SelectionPrompt<FtpSecurity>()
                .Title("Encryption:")
                .HighlightStyle(new Style(Widgets.PrimaryColor))
                .AddChoices(FtpSecurity.Explicit, FtpSecurity.Auto, FtpSecurity.Implicit, FtpSecurity.None)
                .UseConverter(DescribeSecurity));

            if (security == FtpSecurity.Explicit)
            {
                scheme = "ftps";
            }

            profile[ProfileProperties.WebPublishMethod] = "FTP";
            profile[ProfileProperties.PubwizFtpSecurity] = security.ToString();
            profile[ProfileProperties.FtpPassiveMode] = console.Confirm("Use passive mode?") ? "true" : "false";
            profile[ProfileProperties.AllowUntrustedCertificate] = security != FtpSecurity.None && console.Confirm("Allow an untrusted (self-signed) certificate?", defaultValue: false) ? "true" : null;
        }

        profile[ProfileProperties.PublishUrl] = $"{scheme}://{host}:{port}/{remoteFolder.Trim('/')}";
        profile[ProfileProperties.UserName] = userName;

        if (sftp)
        {
            var keyFile = console.Prompt(new TextPrompt<string>("Private key file [dim](leave empty to use a password)[/]:").AllowEmpty());
            profile[ProfileProperties.PubwizSshPrivateKeyFile] = keyFile.Length > 0 ? Path.GetFullPath(keyFile.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))) : null;
        }

        profile[ProfileProperties.DeleteExistingFiles] = console.Confirm("Delete files on the server that aren't part of the app?", defaultValue: false) ? "true" : "false";
        profile[ProfileProperties.PubwizUseAppOffline] = console.Confirm("Take the app offline (app_offline.htm) while uploading? [dim](IIS)[/]", defaultValue: false) ? "true" : null;

        var siteUrl = console.Prompt(new TextPrompt<string>("Site URL [dim](optional, shown after publishing)[/]:").AllowEmpty());
        profile[ProfileProperties.SiteUrlToLaunchAfterPublish] = siteUrl.Length > 0 ? siteUrl : null;

        var targetFramework = CommonPrompts.ChooseTargetFramework(console, project);
        profile[ProfileProperties.LastUsedBuildConfiguration] = CommonPrompts.ChooseConfiguration(console);
        profile[ProfileProperties.LastUsedPlatform] = "Any CPU";
        profile[ProfileProperties.TargetFramework] = project.TargetFrameworks.Count > 1 ? targetFramework : null;
        return profile;
    }

    private static string DescribeSecurity(FtpSecurity security) => security switch
    {
        FtpSecurity.Explicit => "FTPS, explicit TLS [dim](recommended; Azure App Service)[/]",
        FtpSecurity.Auto => "TLS if the server supports it",
        FtpSecurity.Implicit => "FTPS, implicit TLS [dim](port 990)[/]",
        _ => "None [dim](plain FTP, unencrypted)[/]"
    };
}
