using System.IO.Compression;

namespace Pubwiz.Core.Publishing.Steps;

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

    /// <summary>
    /// Marks a file in a zip as executable on Linux (<c>rwxr-xr-x</c>). Zips made on Windows have no Unix permissions,
    /// so a Linux host (Lambda, Elastic Beanstalk) couldn't start an executable from them otherwise.
    /// </summary>
    public static void MarkExecutable(ZipArchiveEntry entry)
    {
        // The high 16 bits hold the Unix mode: a regular file (0o100000) with permissions 0o755.
        const int unixRegularFile = 0b1_000_000_000_000_000;
        const int permissions = 0b111_101_101;
        entry.ExternalAttributes = (unixRegularFile | permissions) << 16;
    }
}
