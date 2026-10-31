namespace Pubwiz.Core.Platform;

/// <summary>
/// Finds executables on disk.
/// </summary>
public static class ExecutableLocator
{
    /// <summary>
    /// Searches <c>PATH</c> for an executable, e.g. <c>az</c>, <c>docker</c> or <c>podman</c>.
    /// </summary>
    /// <param name="name">The name without extension; <c>.exe</c> (and <c>.cmd</c> for scripts like <c>az</c>) is added on Windows.</param>
    /// <returns>The full path, or <c>null</c> when it isn't installed.</returns>
    public static string? FindInPath(string name)
    {
        var candidates = OperatingSystemInfo.IsWindows
            ? [$"{name}.exe", $"{name}.cmd", $"{name}.bat"]
            : new[] { name };

        return EnvironmentVariables.PathFolders
            .SelectMany(folder => candidates.Select(candidate => Path.Combine(folder, candidate)))
            .FirstOrDefault(File.Exists);
    }
}
