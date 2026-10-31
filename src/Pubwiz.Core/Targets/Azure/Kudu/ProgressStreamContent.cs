using System.Net;

namespace Pubwiz.Core.Targets.Azure.Kudu;

/// <summary>
/// HTTP content that streams a file and reports how much of it has been sent.
/// </summary>
/// <param name="stream">The data to send; its length must be known.</param>
/// <param name="onProgress">Called with the fraction sent so far (0 to 1).</param>
public sealed class ProgressStreamContent(Stream stream, Action<double> onProgress) : HttpContent
{
    private const int BufferSize = 81920;

    protected override async Task SerializeToStreamAsync(Stream target, TransportContext? context, CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        var total = stream.Length;
        long sent = 0;

        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            sent += read;
            onProgress(total == 0 ? 1 : (double)sent / total);
        }
    }

    protected override Task SerializeToStreamAsync(Stream target, TransportContext? context) => SerializeToStreamAsync(target, context, CancellationToken.None);

    protected override bool TryComputeLength(out long length)
    {
        length = stream.Length;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            stream.Dispose();
        }

        base.Dispose(disposing);
    }
}
