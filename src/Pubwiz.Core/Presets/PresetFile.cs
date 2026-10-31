using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pubwiz.Core.Presets;

/// <summary>One project in a preset and the profile to publish it with.</summary>
/// <param name="Path">The project file, relative to the preset file's folder.</param>
/// <param name="Profile">The publish profile name.</param>
public sealed record PresetProject(string Path, string Profile);

/// <summary>
/// A saved batch: which projects to publish with which profiles, and how.
/// </summary>
/// <param name="Projects">The projects, published in this order.</param>
/// <param name="Configuration">Overrides each profile's configuration when set.</param>
/// <param name="Parallel">How many projects to publish at once (default 1).</param>
public sealed record PublishPreset(IReadOnlyList<PresetProject> Projects, string? Configuration = null, int? Parallel = null);

/// <summary>
/// The <c>pubwiz.json</c> file holding named presets. It's meant to be committed, so a team (or CI) can run
/// the same batch with <c>dotnet pubwiz --preset name</c>.
/// </summary>
/// <remarks>
/// <code>
/// {
///   "presets": {
///     "prod": {
///       "configuration": "Release",
///       "parallel": 2,
///       "projects": [ { "path": "src/Api/Api.csproj", "profile": "Prod" } ]
///     }
///   }
/// }
/// </code>
/// </remarks>
public sealed class PresetFile
{
    public const string FileName = "pubwiz.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    [JsonPropertyName("presets")]
    public Dictionary<string, PublishPreset> Presets { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Finds <c>pubwiz.json</c> in <paramref name="startDirectory"/> or its parent folders (like <c>global.json</c>).
    /// </summary>
    /// <returns>The file path, or <c>null</c> when there's none.</returns>
    public static string? Find(string startDirectory)
    {
        for (var folder = new DirectoryInfo(Path.GetFullPath(startDirectory)); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <exception cref="InvalidDataException">The file isn't valid JSON.</exception>
    public static PresetFile Load(string path)
    {
        try
        {
            var file = JsonSerializer.Deserialize<PresetFile>(File.ReadAllText(path), JsonOptions) ?? new PresetFile();

            // System.Text.Json fills the dictionary without the case-insensitive comparer.
            return new PresetFile { Presets = new Dictionary<string, PublishPreset>(file.Presets, StringComparer.OrdinalIgnoreCase) };
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path} is not a valid preset file: {ex.Message}", ex);
        }
    }

    /// <summary>Loads the file when it exists, otherwise returns an empty one.</summary>
    public static PresetFile LoadOrCreate(string path) => File.Exists(path) ? Load(path) : new PresetFile();

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions) + Environment.NewLine);

    /// <summary>A file containing only <paramref name="name"/>, e.g. to share or to run with <c>--preset-file</c>.</summary>
    public PresetFile Export(string name) => new() { Presets = new(StringComparer.OrdinalIgnoreCase) { [name] = Presets[name] } };
}
