using System.Runtime.InteropServices;

namespace Pubwiz.Core.Infrastructure.Platform;

/// <summary>
/// Facts about the machine Pubwiz is running on.
/// </summary>
public static class OperatingSystemInfo
{
    private static readonly Lazy<bool> IsWslLazy = new(DetectWsl);

    /// <summary>
    /// The OS part of a runtime identifier: <c>win</c>, <c>linux</c>, <c>osx</c> or <c>unknown</c>.
    /// </summary>
    /// <seealso href="https://learn.microsoft.com/en-us/dotnet/core/rid-catalog"/>
    public static string OsIdentifier { get; } = DetectOsIdentifier();

    /// <summary>
    /// The runtime identifier of the current machine, e.g. <c>linux-x64</c>.
    /// </summary>
    public static string Rid { get; } = $"{OsIdentifier}-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";

    public static bool IsWindows => OperatingSystem.IsWindows();

    public static bool IsMacOS => OperatingSystem.IsMacOS();

    public static bool IsLinux => OperatingSystem.IsLinux();

    /// <summary>
    /// <c>true</c> when running inside the Windows Subsystem for Linux.
    /// </summary>
    public static bool IsWsl => IsWslLazy.Value;

    /// <summary>
    /// Appends <c>.exe</c> on Windows, e.g. <c>dotnet</c> → <c>dotnet.exe</c>.
    /// </summary>
    public static string ToExecutableName(string name) => IsWindows ? $"{name}.exe" : name;

    private static string DetectOsIdentifier()
    {
        if (OperatingSystem.IsWindows())
        {
            return "win";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "osx";
        }

        return "unknown";
    }

    private static bool DetectWsl()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        // WSL kernels identify themselves as "...microsoft-standard-WSL2" in /proc/version.
        const string procVersion = "/proc/version";
        return File.Exists(procVersion) && File.ReadAllText(procVersion).Contains("microsoft", StringComparison.OrdinalIgnoreCase);
    }
}
