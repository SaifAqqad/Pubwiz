using FluentFTP;
using FluentFTP.Exceptions;
using System.Net;
using System.Text;

namespace Pubwiz.Core.Transfer;

/// <summary>How to secure an FTP connection.</summary>
public enum FtpSecurity
{
    /// <summary>Use TLS when the server supports it, otherwise plain FTP.</summary>
    Auto,

    /// <summary>Plain FTP: credentials and files are sent unencrypted.</summary>
    None,

    /// <summary>FTPS with explicit TLS (AUTH TLS on port 21), used by Azure App Service.</summary>
    Explicit,

    /// <summary>FTPS with implicit TLS (usually port 990).</summary>
    Implicit
}

/// <summary>Where and how to connect to an FTP server.</summary>
public sealed record FtpConnection(string Host, int Port, string UserName, string Password, FtpSecurity Security, bool PassiveMode, bool AllowUntrustedCertificate);

/// <summary>
/// Uploads with FTP or FTPS using FluentFTP.
/// </summary>
public sealed class FtpUploader(FtpConnection connection) : IFileUploader
{
    private readonly AsyncFtpClient _client = new(connection.Host, connection.UserName, connection.Password, connection.Port, CreateConfig(connection));

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _client.Connect(cancellationToken);
        }
        catch (Exception ex) when (ex is FtpException or IOException or System.Security.Authentication.AuthenticationException or System.Net.Sockets.SocketException)
        {
            throw new UploadException($"Could not connect to ftp://{connection.Host}:{connection.Port}: {ex.Message}", ex);
        }
    }

    public async Task UploadDirectoryAsync(string localDirectory, string remoteDirectory, UploadOptions options, Action<UploadProgress> onProgress, CancellationToken cancellationToken)
    {
        try
        {
            if (options.UseAppOffline)
            {
                await _client.UploadBytes(Encoding.UTF8.GetBytes(AppOffline.Content), $"{remoteDirectory.TrimEnd('/')}/{AppOffline.FileName}", FtpRemoteExists.Overwrite, createRemoteDir: true, token: cancellationToken);
            }

            // Mirror deletes remote files missing locally; Update only adds and replaces.
            var mode = options.DeleteExtraFiles ? FtpFolderSyncMode.Mirror : FtpFolderSyncMode.Update;
            var progress = new Progress<FtpProgress>(p => onProgress(new UploadProgress(p.FileIndex, p.FileCount, Path.GetRelativePath(localDirectory, p.LocalPath))));

            // The app_offline.htm file must survive a mirror upload until it's removed on purpose at the end.
            var rules = options.UseAppOffline ? new List<FluentFTP.Rules.FtpRule> { new FluentFTP.Rules.FtpFileNameRule(false, [AppOffline.FileName]) } : null;

            await _client.UploadDirectory(localDirectory, remoteDirectory, mode, FtpRemoteExists.Overwrite, FtpVerify.None, rules, progress, cancellationToken);

            if (options.UseAppOffline)
            {
                await _client.DeleteFile($"{remoteDirectory.TrimEnd('/')}/{AppOffline.FileName}", cancellationToken);
            }
        }
        catch (FtpException ex)
        {
            throw new UploadException($"FTP upload failed: {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _client.Disconnect();
        _client.Dispose();
    }

    private static FtpConfig CreateConfig(FtpConnection connection)
    {
        return new FtpConfig
        {
            EncryptionMode = connection.Security switch
            {
                FtpSecurity.None => FtpEncryptionMode.None,
                FtpSecurity.Explicit => FtpEncryptionMode.Explicit,
                FtpSecurity.Implicit => FtpEncryptionMode.Implicit,
                _ => FtpEncryptionMode.Auto
            },
            ValidateAnyCertificate = connection.AllowUntrustedCertificate,
            DataConnectionType = connection.PassiveMode ? FtpDataConnectionType.AutoPassive : FtpDataConnectionType.AutoActive
        };
    }
}
