using System.IO.Compression;

namespace Pubwiz.Core.Transfer;

/// <summary>
/// Zips a publish folder for upload (Kudu zip deploy, WebJobs, ...).
/// </summary>
public static class ZipPackager
{
    /// <summary>
    /// Zips the contents of <paramref name="sourceDirectory"/> (not the folder itself, as App Service expects)
    /// into <paramref name="zipPath"/>, replacing an existing file.
    /// </summary>
    public static void CreateFromDirectory(string sourceDirectory, string zipPath)
    {
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        ZipFile.CreateFromDirectory(sourceDirectory, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
    }
}
