namespace Pubwiz.Core.Transfer;

/// <summary>
/// Options for uploading a publish folder.
/// </summary>
/// <param name="DeleteExtraFiles">Delete remote files that aren't in the local folder (mirror).</param>
/// <param name="UseAppOffline">
/// Upload <c>app_offline.htm</c> first and remove it at the end. IIS (ASP.NET Core Module) then stops the app,
/// so its DLLs aren't locked while they're replaced.
/// </param>
public sealed record UploadOptions(bool DeleteExtraFiles, bool UseAppOffline);

/// <summary>Upload progress.</summary>
/// <param name="FilesDone">Files uploaded so far.</param>
/// <param name="FileCount">Files to upload in total.</param>
/// <param name="CurrentFile">The file being uploaded, relative to the publish folder.</param>
public readonly record struct UploadProgress(int FilesDone, int FileCount, string? CurrentFile)
{
    public double Fraction => FileCount == 0 ? 1 : (double)FilesDone / FileCount;
}

/// <summary>
/// Uploads a folder to a server (FTP, FTPS, SFTP, ...).
/// </summary>
public interface IFileUploader : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>Uploads the contents of <paramref name="localDirectory"/> into <paramref name="remoteDirectory"/>.</summary>
    Task UploadDirectoryAsync(string localDirectory, string remoteDirectory, UploadOptions options, Action<UploadProgress> onProgress, CancellationToken cancellationToken);
}

/// <summary>Thrown when an upload fails (connection refused, login failed, permission denied, ...).</summary>
public sealed class UploadException(string message, Exception? innerException = null) : Exception(message, innerException);
