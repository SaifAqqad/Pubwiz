namespace Pubwiz.Core.Platform;

/// <summary>
/// Typed access to the environment variables Pubwiz reads.
/// </summary>
public static class EnvironmentVariables
{
    /// <summary>
    /// Full path of the dotnet executable, set by the dotnet host for child processes.
    /// </summary>
    /// <seealso href="https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables#dotnet_host_path"/>
    public static string? DotnetHostPath => Get("DOTNET_HOST_PATH");

    /// <summary>
    /// The folder the .NET SDK/runtime is installed in.
    /// </summary>
    public static string? DotnetRoot => Get("DOTNET_ROOT");

    /// <summary>
    /// <c>true</c> when running in a CI system (most CI providers set <c>CI=true</c>).
    /// </summary>
    public static bool IsCi => string.Equals(Get("CI"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The folders in <c>PATH</c>. Inside WSL, Windows folders (<c>/mnt/c/...</c>) are skipped
    /// so Windows executables like <c>dotnet.exe</c> are not picked up by mistake.
    /// </summary>
    public static IReadOnlyList<string> PathFolders
    {
        get
        {
            var folders = Get("PATH")?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
            return OperatingSystemInfo.IsWsl
                ? folders.Where(f => !f.StartsWith("/mnt/", StringComparison.Ordinal)).ToArray()
                : folders;
        }
    }

    private static string? Get(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
