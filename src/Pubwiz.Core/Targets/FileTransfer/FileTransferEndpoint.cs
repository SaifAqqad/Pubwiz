using Pubwiz.Core.Profiles;
using Pubwiz.Core.Transfer;

namespace Pubwiz.Core.Targets.FileTransfer;

/// <summary>
/// The server and folder an FTP, FTPS or SFTP profile uploads to.
/// </summary>
/// <param name="IsSftp"><c>true</c> for SFTP, <c>false</c> for FTP/FTPS.</param>
/// <param name="Host">The server name.</param>
/// <param name="Port">The port, defaulted from the scheme.</param>
/// <param name="RemoteDirectory">The folder to upload into, e.g. <c>/site/wwwroot</c>.</param>
/// <param name="FtpSecurity">For FTP: how to use TLS.</param>
public sealed record FileTransferEndpoint(bool IsSftp, string Host, int Port, string RemoteDirectory, FtpSecurity FtpSecurity)
{
    /// <summary>
    /// Reads the endpoint from <c>PublishUrl</c> (<c>ftp://</c>, <c>ftps://</c> or <c>sftp://</c>) plus the optional <c>FtpSitePath</c>.
    /// <c>ftps://</c> means explicit TLS, which is what Azure's .publishsettings files use.
    /// </summary>
    /// <exception cref="ArgumentException">The profile has no valid URL.</exception>
    public static FileTransferEndpoint FromProfile(PublishProfile profile)
    {
        var publishUrl = profile[ProfileProperties.PublishUrl];
        if (string.IsNullOrWhiteSpace(publishUrl) || !Uri.TryCreate(publishUrl.Contains("://") ? publishUrl : $"ftp://{publishUrl}", UriKind.Absolute, out var url))
        {
            throw new ArgumentException($"The profile '{profile.Name}' has no valid {ProfileProperties.PublishUrl} (expected ftp://, ftps:// or sftp://).", nameof(profile));
        }

        var isSftp = url.Scheme == "sftp";
        var security = url.Scheme == "ftps" ? FtpSecurity.Explicit : FtpSecurity.Auto;
        if (Enum.TryParse<FtpSecurity>(profile[ProfileProperties.PubwizFtpSecurity], ignoreCase: true, out var configured))
        {
            security = configured;
        }

        // Uri reports the scheme's default port, or -1 for schemes it doesn't know (ftps, sftp).
        var port = url.IsDefaultPort || url.Port < 0
            ? isSftp ? 22 : security == FtpSecurity.Implicit ? 990 : 21
            : url.Port;

        var directory = CombinePaths(Uri.UnescapeDataString(url.AbsolutePath), profile[ProfileProperties.FtpSitePath]);
        return new FileTransferEndpoint(isSftp, url.Host, port, directory, security);
    }

    public override string ToString() => $"{(IsSftp ? "sftp" : "ftp")}://{Host}:{Port}{RemoteDirectory}";

    private static string CombinePaths(string basePath, string? sitePath)
    {
        var combined = string.IsNullOrWhiteSpace(sitePath) ? basePath : $"{basePath.TrimEnd('/')}/{sitePath.Trim('/')}";
        return "/" + combined.Trim('/');
    }
}
