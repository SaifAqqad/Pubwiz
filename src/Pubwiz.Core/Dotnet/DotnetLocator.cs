using Pubwiz.Core.Platform;

namespace Pubwiz.Core.Dotnet;

/// <summary>
/// Finds the <c>dotnet</c> executable, even when it isn't in <c>PATH</c>.
/// </summary>
public static class DotnetLocator
{
    private static readonly string ExecutableName = OperatingSystemInfo.ToExecutableName("dotnet");

    /// <summary>
    /// Returns the full path to the dotnet executable, or <c>null</c> when none can be found.
    /// </summary>
    /// <remarks>
    /// Checked in order: <c>DOTNET_HOST_PATH</c>, <c>DOTNET_ROOT</c>, <c>PATH</c>,
    /// the installation running Pubwiz itself, then the default install folders.
    /// </remarks>
    public static string? Find() => GetCandidates().FirstOrDefault(File.Exists);

    private static IEnumerable<string> GetCandidates()
    {
        if (EnvironmentVariables.DotnetHostPath is { } hostPath)
        {
            yield return hostPath;
        }

        if (EnvironmentVariables.DotnetRoot is { } root)
        {
            yield return Path.Combine(root, ExecutableName);
        }

        if (ExecutableLocator.FindInPath("dotnet") is { } pathMatch)
        {
            yield return pathMatch;
        }

        if (GetRunningInstallationRoot() is { } runningRoot)
        {
            yield return Path.Combine(runningRoot, ExecutableName);
        }

        foreach (var folder in GetDefaultInstallFolders())
        {
            yield return Path.Combine(folder, ExecutableName);
        }
    }

    /// <summary>
    /// When Pubwiz runs as a dotnet tool, the runtime it runs on lives in
    /// <c>{dotnet root}/shared/Microsoft.NETCore.App/{version}/</c>, so the root is three folders up.
    /// Returns <c>null</c> for self-contained builds, which carry their own runtime.
    /// </summary>
    private static string? GetRunningInstallationRoot()
    {
        var runtimeFolder = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (string.IsNullOrEmpty(runtimeFolder))
        {
            return null;
        }

        var root = Directory.GetParent(runtimeFolder)?.Parent?.Parent;
        return root is not null && root.GetDirectories("sdk").Length > 0 ? root.FullName : null;
    }

    private static IEnumerable<string> GetDefaultInstallFolders()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".dotnet");

        if (OperatingSystemInfo.IsWindows)
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
        }
        else if (OperatingSystemInfo.IsMacOS)
        {
            yield return "/usr/local/share/dotnet";
        }
        else
        {
            yield return "/usr/share/dotnet";
            yield return "/usr/lib/dotnet";
            yield return "/usr/lib64/dotnet";
        }
    }
}
