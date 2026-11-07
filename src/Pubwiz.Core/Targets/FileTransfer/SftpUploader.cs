using Renci.SshNet;
using Renci.SshNet.Common;
using System.Text;

namespace Pubwiz.Core.Targets.FileTransfer;

/// <summary>
/// Where and how to connect to an SFTP server. Set either <paramref name="Password"/> or <paramref name="PrivateKeyFile"/>.
/// </summary>
/// <param name="PrivateKeyFile">An OpenSSH/PEM private key file.</param>
/// <param name="PrivateKeyPassphrase">The key's passphrase, if it has one.</param>
public sealed record SftpConnection(string Host, int Port, string UserName, string? Password, string? PrivateKeyFile, string? PrivateKeyPassphrase)
{
    /// <summary>The SSH.NET connection settings, shared by the uploader and <see cref="SshCommandStep"/>.</summary>
    internal ConnectionInfo ToConnectionInfo()
    {
        AuthenticationMethod method = PrivateKeyFile is not null
            ? new PrivateKeyAuthenticationMethod(UserName, new PrivateKeyFile(PrivateKeyFile, PrivateKeyPassphrase))
            : new PasswordAuthenticationMethod(UserName, Password ?? string.Empty);

        return new ConnectionInfo(Host, Port, UserName, method);
    }
}

/// <summary>
/// Uploads over SSH (SFTP) using SSH.NET.
/// </summary>
/// <remarks>
/// SFTP has no "sync folder" command, so this walks the local folder, uploads every file
/// (creating folders as needed) and, when asked, deletes remote files that don't exist locally.
/// </remarks>
public sealed class SftpUploader(SftpConnection connection) : IFileUploader
{
    private readonly SftpClient _client = new(connection.ToConnectionInfo());

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
        // "/" stays "/": an empty path would mean the user's home folder to the server.
        var root = remoteDirectory.TrimEnd('/') is { Length: > 0 } trimmed ? trimmed : "/";
        var prefix = root.EndsWith('/') ? root : root + "/";
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
                await _client.UploadFileAsync(page, prefix + AppOffline.FileName, cancellationToken);
            }

            for (var i = 0; i < files.Count; i++)
            {
                onProgress(new UploadProgress(i, files.Count, files[i]));
                await UploadFileAsync(Path.Combine(localDirectory, files[i]), prefix + files[i], cancellationToken);
            }

            onProgress(new UploadProgress(files.Count, files.Count, null));

            if (options.DeleteExtraFiles)
            {
                FindExtraFiles(root, files.ToHashSet(StringComparer.Ordinal), keep: options.UseAppOffline ? AppOffline.FileName : null, ListFolder)
                    .ToList()
                    .ForEach(_client.DeleteFile);
            }

            if (options.UseAppOffline)
            {
                _client.DeleteFile(prefix + AppOffline.FileName);
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

    private IEnumerable<(string Name, bool IsDirectory)> ListFolder(string folder) =>
        _client.ListDirectory(folder).Select(entry => (entry.Name, entry.IsDirectory));

    /// <summary>The remote files under <paramref name="root"/> that aren't in <paramref name="localFiles"/>, as full paths.</summary>
    /// <remarks>
    /// Paths are built from <paramref name="root"/> and the entry names, not taken from the server's listing: the server
    /// reports resolved paths (symlinks followed), which wouldn't start with <paramref name="root"/>.
    /// </remarks>
    /// <param name="root">The remote folder.</param>
    /// <param name="localFiles">Paths relative to <paramref name="root"/> that must stay.</param>
    /// <param name="keep">A file in the root to leave alone (app_offline.htm).</param>
    /// <param name="list">Lists a remote folder's entries.</param>
    internal static IEnumerable<string> FindExtraFiles(string root, HashSet<string> localFiles, string? keep, Func<string, IEnumerable<(string Name, bool IsDirectory)>> list)
    {
        var prefix = root.EndsWith('/') ? root : root + "/";
        var pending = new Stack<string>([string.Empty]);
        while (pending.TryPop(out var folder))
        {
            foreach (var (name, isDirectory) in list(folder.Length == 0 ? root : prefix + folder).Where(e => e.Name is not ("." or "..")))
            {
                var relative = folder.Length == 0 ? name : $"{folder}/{name}";
                if (isDirectory)
                {
                    pending.Push(relative);
                }
                else if (!localFiles.Contains(relative) && relative != keep)
                {
                    yield return prefix + relative;
                }
            }
        }
    }
}
