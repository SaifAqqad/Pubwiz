using Pubwiz.Core.Infrastructure.Platform;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pubwiz.Core.Presets;

/// <summary>What was last published from a folder: the projects (full paths), their profiles and order, and the batch options.</summary>
/// <param name="PublishedAt">When it was published.</param>
/// <param name="Os">The OS chosen to build for (see <see cref="PublishPreset.Os"/>).</param>
/// <param name="Arch">The architecture chosen to build for (see <see cref="PublishPreset.Arch"/>).</param>
public sealed record RecentRun(DateTimeOffset PublishedAt, IReadOnlyList<PresetProject> Projects, string? Configuration = null, int? Parallel = null,
    string? Os = null, string? Arch = null)
{
    /// <summary>The run as a preset, to resolve and publish it the same way.</summary>
    public PublishPreset ToPreset() => new(Projects, Configuration, Parallel, Os, Arch);
}

/// <summary>
/// Remembers the last publish per start folder (or file), so the next run can offer to publish the same again.
/// Kept in the user's config folder next to <c>servers.json</c>, not in the repository.
/// </summary>
/// <remarks>
/// It's a convenience: a missing, unreadable or invalid file is treated as empty, and failing to save is ignored.
/// </remarks>
public sealed class RecentRunStore(string filePath)
{
    /// <summary>How many folders are remembered; the oldest are forgotten first.</summary>
    public const int MaxRuns = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>The file in the user's config folder (see <see cref="Targets.ServerLogins.ServerCredentialStore.DefaultFilePath"/>).</summary>
    public static string DefaultFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify), "pubwiz", "recent.json");

    public string FilePath { get; } = filePath;

    /// <summary>The last run started from <paramref name="startPath"/>, or <c>null</c>.</summary>
    /// <param name="startPath">The folder, solution or project Pubwiz was started with.</param>
    public RecentRun? Find(string startPath) =>
        Load().TryGetValue(ToKey(startPath), out var run) && run.Projects is { Count: > 0 } projects && projects.All(IsComplete) ? run : null;

    /// <summary>Remembers <paramref name="run"/> as the last one started from <paramref name="startPath"/>.</summary>
    public void Save(string startPath, RecentRun run)
    {
        var runs = Load();
        runs[ToKey(startPath)] = run;

        var kept = runs.OrderByDescending(r => r.Value.PublishedAt).Take(MaxRuns).ToDictionary(r => r.Key, r => r.Value, Comparer);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new RecentRunFile { Runs = kept }, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth failing a publish over.
        }
    }

    private static StringComparer Comparer => OperatingSystemInfo.PathComparison == StringComparison.OrdinalIgnoreCase
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static string ToKey(string startPath) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(startPath));

    private static bool IsComplete(PresetProject? project) =>
        project is { Path.Length: > 0, Profile.Length: > 0 } && Path.IsPathFullyQualified(project.Path);

    private Dictionary<string, RecentRun> Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<RecentRunFile>(File.ReadAllText(FilePath), JsonOptions)?.Runs is { } runs)
            {
                // Added one by one: keys that differ only in case would make the constructor throw.
                var result = new Dictionary<string, RecentRun>(Comparer);
                foreach (var (key, run) in runs.Where(r => r.Value is not null))
                {
                    result[key] = run;
                }

                return result;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable: start over.
        }

        return new Dictionary<string, RecentRun>(Comparer);
    }

    private sealed class RecentRunFile
    {
        public Dictionary<string, RecentRun>? Runs { get; init; }
    }
}
