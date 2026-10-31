using System.Reflection;

namespace Pubwiz.Tool;

/// <summary>
/// Build-time values (version, project URL, command name) embedded as assembly metadata by the csproj.
/// </summary>
public static class ToolInfo
{
    private static readonly Dictionary<string, string?> Metadata = typeof(ToolInfo).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .ToDictionary(a => a.Key, a => a.Value);

    public static string Version { get; } = Metadata["Version"]!;

    public static string ProjectUrl { get; } = Metadata["PackageProjectUrl"]!;

    /// <summary>
    /// How users invoke the tool, e.g. <c>dotnet pubwiz</c>.
    /// </summary>
    public static string CommandName { get; } = Metadata["CommandName"]!.Replace("dotnet-", "dotnet ");
}
