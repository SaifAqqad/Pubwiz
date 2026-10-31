using Microsoft.Win32;
using Pubwiz.Core.Platform;
using System.Runtime.Versioning;

namespace Pubwiz.Core.MsDeploy;

/// <summary>
/// Finds <c>msdeploy.exe</c>, the official Web Deploy command line tool. It only exists on Windows.
/// </summary>
public static class MsDeployLocator
{
    public const string ExecutableName = "msdeploy.exe";

    public const string DownloadUrl = "https://www.iis.net/downloads/microsoft/web-deploy";

    /// <summary>Where the Web Deploy installer records its folder (the same key the .NET SDK reads).</summary>
    private const string InstallKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\IIS Extensions\MSDeploy\3";

    /// <summary>The full path to msdeploy.exe, or <c>null</c> when Web Deploy isn't installed (always on Linux and macOS).</summary>
    /// <remarks>Checked in order: the installer's registry entry, the default install folders, then <c>PATH</c>.</remarks>
    public static string? Find() => OperatingSystem.IsWindows() ? GetWindowsCandidates().FirstOrDefault(File.Exists) : null;

    /// <summary>A short explanation of why Web Deploy can't be used here, or <c>null</c> when msdeploy.exe was found.</summary>
    public static string? GetMissingReason() => Find() is not null
        ? null
        : OperatingSystemInfo.IsWindows
            ? $"Web Deploy isn't installed. Install it from {DownloadUrl}."
            : "Web Deploy (msdeploy.exe) only runs on Windows. Publish from Windows, or use FTP/SFTP from this machine.";

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> GetWindowsCandidates()
    {
        if (Registry.GetValue(InstallKey, "InstallPath", null) is string installPath)
        {
            yield return Path.Combine(installPath, ExecutableName);
        }

        foreach (var programFiles in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            yield return Path.Combine(Environment.GetFolderPath(programFiles), "IIS", "Microsoft Web Deploy V3", ExecutableName);
        }

        if (ExecutableLocator.FindInPath("msdeploy") is { } pathMatch)
        {
            yield return pathMatch;
        }
    }
}
