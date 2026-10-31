using Renci.SshNet;
using Renci.SshNet.Common;
using System.Text;

namespace Pubwiz.Core.Targets.FileTransfer;

/// <summary>
/// Where and how to connect to an SFTP server. Set either <paramref name="Password"/> or <paramref name="PrivateKeyFile"/>.
/// </summary>
/// <param name="PrivateKeyFile">An OpenSSH/PEM private key file.</param>
/// <param name="PrivateKeyPassphrase">The key's passphrase, if it has one.</param>
public sealed record SftpConnection(string Host, int Port, string UserName, string? Password, string? PrivateKeyFile, string? PrivateKeyPassphrase);

/// <summary>
/// Uploads over SSH (SFTP) using SSH.NET.
/// </summary>
/// <remarks>
/// SFTP has no "sync folder" command, so this walks the local folder, uploads every file
/// (creating folders as needed) and, when asked, deletes remote files that don't exist locally.
/// </remarks>
public sealed class SftpUploader(SftpConnection connection) : IFileUploader
{
    private readonly SftpClient _client = new(CreateConnectionInfo(connection));

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _client.ConnectAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is SshException or System.Net.Sockets.SocketException or IOException)
        {
            throw new UploadException($"Could not connect to sftp://{connection.Host}:{connection.Port}: {ex.Message}", ex);
        }
    }

    public async Task UploadDirectoryAsync(string localDirectory, string remoteDirectory, UploadOptions options, Action<UploadProgress> onProgress, CancellationToken cancellationToken)
    {
        var root = remoteDirectory.TrimEnd('/');
        var files = Directory.GetFiles(localDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(localDirectory, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        try
        {
            EnsureDirectory(root);
            if (options.UseAppOffline)
            {
                using var page = new MemoryStream(Encoding.UTF8.GetBytes(AppOffline.Content));
                await _client.UploadFileAsync(page, $"{root}/{AppOffline.FileName}", cancellationToken);
            }

            for (var i = 0; i < files.Count; i++)
            {
                onProgress(new UploadProgress(i, files.Count, files[i]));
                await UploadFileAsync(Path.Combine(localDirectory, files[i]), $"{root}/{files[i]}", cancellationToken);
            }

            onProgress(new UploadProgress(files.Count, files.Count, null));

            if (options.DeleteExtraFiles)
            {
                DeleteExtraFiles(root, files.ToHashSet(StringComparer.Ordinal), keep: options.UseAppOffline ? AppOffline.FileName : null);
            }

            if (options.UseAppOffline)
            {
                _client.DeleteFile($"{root}/{AppOffline.FileName}");
            }
        }
        catch (Exception ex) when (ex is SshException or SftpPermissionDeniedException or SftpPathNotFoundException)
        {
            throw new UploadException($"SFTP upload failed: {ex.Message}", ex);
        }
    }

    public ValueTask DisposeAsync()
    {
        _client.Disconnect();
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task UploadFileAsync(string localPath, string remotePath, CancellationToken cancellationToken)
    {
        EnsureDirectory(remotePath[..remotePath.LastIndexOf('/')]);
        await using var stream = File.OpenRead(localPath);
        await _client.UploadFileAsync(stream, remotePath, cancellationToken);
    }

    /// <summary>Creates a folder and its parents (like <c>mkdir -p</c>).</summary>
    private void EnsureDirectory(string path)
    {
        var current = path.StartsWith('/') ? "/" : string.Empty;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.Length == 0 || current.EndsWith('/') ? current + part : $"{current}/{part}";
            if (!_client.Exists(current))
            {
                _client.CreateDirectory(current);
            }
        }
    }

    /// <param name="root">The remote folder.</param>
    /// <param name="localFiles">Paths relative to <paramref name="root"/> that must stay.</param>
    /// <param name="keep">A file in the root to leave alone (app_offline.htm).</param>
    private void DeleteExtraFiles(string root, HashSet<string> localFiles, string? keep)
    {
        var pending = new Stack<string>([root]);
        while (pending.TryPop(out var folder))
        {
            foreach (var entry in _client.ListDirectory(folder).Where(e => e.Name is not ("." or "..")))
            {
                var relative = entry.FullName[(root.Length + 1)..];
                if (entry.IsDirectory)
                {
                    pending.Push(entry.FullName);
                }
                else if (!localFiles.Contains(relative) && relative != keep)
                {
                    _client.DeleteFile(entry.FullName);
                }
            }
        }
    }

    private static ConnectionInfo CreateConnectionInfo(SftpConnection connection)
    {
        AuthenticationMethod method = connection.PrivateKeyFile is not null
            ? new PrivateKeyAuthenticationMethod(connection.UserName, new PrivateKeyFile(connection.PrivateKeyFile, connection.PrivateKeyPassphrase))
            : new PasswordAuthenticationMethod(connection.UserName, connection.Password ?? string.Empty);

        return new ConnectionInfo(connection.Host, connection.Port, connection.UserName, method);
    }
}
