
namespace Pubwiz.Core.Publishing.Steps;

/// <summary>
/// Zips the publish folder into <paramref name="zipPath"/> and stores it as the job's package.
/// </summary>
public sealed class PackageZipStep(string zipPath) : PublishStep("Package")
{
    public override Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
    {
        var folder = context.OutputDirectory ?? throw new InvalidOperationException("Nothing was published to package.");

        ZipPackager.CreateFromDirectory(folder, zipPath);
        context.PackagePath = zipPath;
        context.Log.Info($"Created {zipPath} ({new FileInfo(zipPath).Length / 1024d / 1024d:0.0} MB)");
        return Task.CompletedTask;
    }
}
