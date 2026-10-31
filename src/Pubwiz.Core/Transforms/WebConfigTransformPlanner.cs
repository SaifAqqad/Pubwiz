namespace Pubwiz.Core.Transforms;

/// <summary>
/// Why a transform file is applied.
/// </summary>
public enum TransformKind
{
    /// <summary><c>web.{Configuration}.config</c>, e.g. web.Release.config.</summary>
    Configuration,

    /// <summary><c>web.{Profile}.config</c>.</summary>
    Profile,

    /// <summary><c>web.{Environment}.config</c>, e.g. web.Staging.config.</summary>
    Environment,

    /// <summary><c>Properties/PublishProfiles/{Profile}.transform</c>.</summary>
    ProfileFile,

    /// <summary>A file passed explicitly.</summary>
    Custom
}

/// <summary>A transform file to apply, in order.</summary>
/// <param name="Kind">Why it applies.</param>
/// <param name="FilePath">The file as it's named on disk, e.g. <c>Web.Release.config</c>.</param>
/// <param name="FoundByDotnetPublish">
/// Whether <c>dotnet publish</c> finds the file too. The SDK checks the exact name (<c>web.Release.config</c>), so a
/// file with other casing is skipped on case-sensitive file systems (usually Linux), but not on Windows or macOS.
/// </param>
public sealed record PlannedTransform(TransformKind Kind, string FilePath, bool FoundByDotnetPublish = true);

/// <summary>
/// Works out which web.config transforms apply, in the same order as <c>dotnet publish</c>
/// (Microsoft.NET.Sdk.Publish.TransformFiles.targets): configuration, profile, environment, then the profile's .transform file.
/// </summary>
public static class WebConfigTransformPlanner
{
    /// <param name="projectDirectory">The folder containing web.config and its transforms.</param>
    /// <param name="configuration">e.g. Release.</param>
    /// <param name="profileName">The publish profile name, if any.</param>
    /// <param name="environmentName">The ASP.NET Core environment (<c>EnvironmentName</c>), if any.</param>
    /// <returns>Only the transforms whose files exist.</returns>
    public static IReadOnlyList<PlannedTransform> Plan(string projectDirectory, string configuration, string? profileName, string? environmentName)
    {
        var candidates = new List<PlannedTransform>
        {
            new(TransformKind.Configuration, Path.Combine(projectDirectory, $"web.{configuration}.config"))
        };

        if (!string.IsNullOrWhiteSpace(profileName))
        {
            candidates.Add(new PlannedTransform(TransformKind.Profile, Path.Combine(projectDirectory, $"web.{profileName}.config")));
        }

        if (!string.IsNullOrWhiteSpace(environmentName))
        {
            candidates.Add(new PlannedTransform(TransformKind.Environment, Path.Combine(projectDirectory, $"web.{environmentName}.config")));
        }

        if (!string.IsNullOrWhiteSpace(profileName))
        {
            candidates.Add(new PlannedTransform(TransformKind.ProfileFile, Path.Combine(projectDirectory, "Properties", "PublishProfiles", $"{profileName}.transform")));
        }

        var found = new List<PlannedTransform>();
        foreach (var candidate in candidates)
        {
            if (FindFile(candidate.FilePath) is { } actualPath)
            {
                // The same check the SDK makes; it ignores case wherever the file system does.
                found.Add(candidate with { FilePath = actualPath, FoundByDotnetPublish = File.Exists(candidate.FilePath) });
            }
        }

        // The same file can match twice (e.g. a profile named "Release"); apply it once, like the SDK does on Windows.
        return found.DistinctBy(t => t.FilePath).ToList();
    }

    /// <summary>
    /// Finds a file ignoring case (projects written on Windows often say "Web.Release.config" while the SDK looks for
    /// "web.Release.config") and returns it with its name as stored on disk, preferring an exact match.
    /// </summary>
    /// <remarks>
    /// The folder is listed instead of calling <see cref="File.Exists"/>, which on case-insensitive file systems
    /// (Windows, macOS) would also match but return the name that was asked for.
    /// </remarks>
    private static string? FindFile(string path)
    {
        var folder = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(folder))
        {
            return null;
        }

        var name = Path.GetFileName(path);
        var matches = Directory.EnumerateFiles(folder)
            .Where(file => string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.FirstOrDefault(file => string.Equals(Path.GetFileName(file), name, StringComparison.Ordinal)) ?? matches.FirstOrDefault();
    }
}
