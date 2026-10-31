using Pubwiz.Core.Publishing;

namespace Pubwiz.Core.Targets.FileTransfer;

/// <summary>
/// Uploads the publish folder with an <see cref="IFileUploader"/> (FTP, SFTP, ...), reporting progress per file.
/// </summary>
/// <param name="name">Step name, e.g. "Upload (FTP)".</param>
/// <param name="createUploader">Creates the uploader; called when the step runs, so connections aren't opened early.</param>
/// <param name="remoteDirectory">The folder on the server.</param>
/// <param name="options">Mirror and app_offline.htm options.</param>
public sealed class UploadStep(string name, Func<IFileUploader> createUploader, string remoteDirectory, UploadOptions options) : PublishStep(name)
{
    public override async Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
    {
        var localDirectory = context.OutputDirectory ?? throw new InvalidOperationException("Nothing was published to upload.");

        await using var uploader = createUploader();
        context.ReportDetail("connecting...");
        await uploader.ConnectAsync(cancellationToken);

        var lastLogged = -1;
        await uploader.UploadDirectoryAsync(localDirectory, remoteDirectory, options, progress =>
        {
            context.ReportProgress(progress.Fraction);
            context.ReportDetail(progress.CurrentFile);

            // One log line per file is plenty; progress callbacks can fire many times per file.
            if (progress.CurrentFile is not null && progress.FilesDone != lastLogged)
            {
                lastLogged = progress.FilesDone;
                context.Log.Info($"[{progress.FilesDone + 1}/{progress.FileCount}] {progress.CurrentFile}");
            }
        }, cancellationToken);

        context.Log.Info($"Uploaded {localDirectory} to {remoteDirectory}");
    }
}
