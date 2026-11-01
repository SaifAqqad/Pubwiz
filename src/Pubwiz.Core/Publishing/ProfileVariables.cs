using Pubwiz.Core.Profiles;
using System.Text.RegularExpressions;

namespace Pubwiz.Core.Publishing;

/// <summary>
/// Fills in the MSBuild variables that Visual Studio puts in profile paths, e.g.
/// <c>bin\$(Configuration)\$(TargetFramework)\publish\</c>, the way MSBuild would. Without this, a path would be used
/// literally and create a folder named <c>$(Configuration)</c>.
/// </summary>
public static partial class ProfileVariables
{
    /// <summary>The variables <see cref="Expand"/> knows (case-insensitive, like MSBuild).</summary>
    public static IReadOnlyList<string> Known { get; } =
    [
        "Configuration", "TargetFramework", "RuntimeIdentifier", "Platform",
        "MSBuildProjectDirectory", "ProjectDir", "MSBuildProjectName", "ProjectName", "MSBuildThisFileDirectory"
    ];

    [GeneratedRegex(@"\$\(([A-Za-z_][A-Za-z0-9_.\-]*)\)")]
    private static partial Regex VariablePattern();

    /// <summary>Replaces the known variables in <paramref name="value"/>; unknown ones are left as they are.</summary>
    public static string Expand(string value, PublishRequest request) =>
        VariablePattern().Replace(value, match => GetValue(match.Groups[1].Value, request) ?? match.Value);

    /// <summary>The first variable in <paramref name="value"/> that <see cref="Expand"/> can't fill in, or <c>null</c>.</summary>
    public static string? FindUnknown(string? value) =>
        value is null
            ? null
            : VariablePattern().Matches(value).Select(m => m.Groups[1].Value).FirstOrDefault(name => !Known.Contains(name, StringComparer.OrdinalIgnoreCase));

    private static string? GetValue(string name, PublishRequest request)
    {
        var project = request.Project;
        var profile = request.Profile;
        var projectDirectory = Path.TrimEndingDirectorySeparator(project.Directory);

        return name.ToUpperInvariant() switch
        {
            "CONFIGURATION" => request.EffectiveConfiguration,
            "TARGETFRAMEWORK" => profile[ProfileProperties.TargetFramework] ?? (project.TargetFrameworks.Count == 1 ? project.TargetFrameworks[0] : string.Empty),
            "RUNTIMEIDENTIFIER" => profile[ProfileProperties.RuntimeIdentifier] ?? string.Empty,
            "PLATFORM" => (profile[ProfileProperties.LastUsedPlatform] ?? "AnyCPU").Replace(" ", string.Empty, StringComparison.Ordinal),
            "MSBUILDPROJECTDIRECTORY" => projectDirectory,
            "PROJECTDIR" => projectDirectory + Path.DirectorySeparatorChar,
            "MSBUILDPROJECTNAME" or "PROJECTNAME" => project.Name,
            "MSBUILDTHISFILEDIRECTORY" => (profile.FilePath is { } file ? Path.GetDirectoryName(Path.GetFullPath(file))! : projectDirectory) + Path.DirectorySeparatorChar,
            _ => null
        };
    }
}
