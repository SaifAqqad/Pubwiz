using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pubwiz.Core.Presets;

/// <summary>One project in a preset and the profile to publish it with.</summary>
/// <param name="Path">The project file, relative to the preset file's folder.</param>
/// <param name="Profile">The publish profile name.</param>
/// <param name="Order">
/// When it's published: lower numbers first, and projects with the same number at the same time
/// (see <see cref="Publishing.PublishOrder"/>). Presets saved before orders existed have none.
/// </param>
public sealed record PresetProject(string Path, string Profile, int? Order = null);

/// <summary>
/// A saved batch: which projects to publish with which profiles, and how.
/// </summary>
/// <param name="Projects">The projects, published by their <see cref="PresetProject.Order"/> (or in this order when they have none).</param>
/// <param name="Configuration">Overrides each profile's configuration when set.</param>
/// <param name="Parallel">
/// The most projects to publish at once within a step (default: all of the step). Without orders, how many projects
/// to publish at once (default 1).
/// </param>
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
///       "projects": [
///         { "path": "src/Api/Api.csproj", "profile": "Prod", "order": 1 },
///         { "path": "src/Worker/Worker.csproj", "profile": "Prod", "order": 1 },
///         { "path": "src/Web/Web.csproj", "profile": "Prod", "order": 2 }
///       ]
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

    /// <exception cref="InvalidDataException">The file isn't valid JSON, or a preset is missing something it needs.</exception>
    public static PresetFile Load(string path)
    {
        PresetFile? file;
        try
        {
            file = JsonSerializer.Deserialize<PresetFile>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path} is not a valid preset file: {ex.Message}", ex);
        }

        var presets = file?.Presets ?? [];
        if (Validate(presets) is { } problem)
        {
            throw new InvalidDataException($"{path} is not a valid preset file: {problem}");
        }

        // System.Text.Json fills the dictionary without the case-insensitive comparer.
        return new PresetFile { Presets = new Dictionary<string, PublishPreset>(presets, StringComparer.OrdinalIgnoreCase) };
    }

    /// <summary>What's wrong with a hand-edited file (JSON can leave out anything), or <c>null</c> when it's usable.</summary>
    private static string? Validate(Dictionary<string, PublishPreset> presets)
    {
        if (presets.Keys.GroupBy(name => name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
        {
            return $"the presets {string.Join(" and ", duplicate.Select(name => $"'{name}'"))} differ only in case; rename one of them.";
        }

        foreach (var (name, preset) in presets)
        {
            if (preset?.Projects is null)
            {
                return $"the preset '{name}' has no \"projects\" list.";
            }

            for (var i = 0; i < preset.Projects.Count; i++)
            {
                var project = preset.Projects[i];
                var missing = project switch
                {
                    null => "is empty",
                    { Path: null or "" } => "has no \"path\"",
                    { Profile: null or "" } => "has no \"profile\"",
                    _ => null
                };

                if (missing is not null)
                {
                    return $"project {i + 1} of the preset '{name}' {missing}.";
                }
            }
        }

        return null;
    }

    /// <summary>Loads the file when it exists, otherwise returns an empty one.</summary>
    public static PresetFile LoadOrCreate(string path) => File.Exists(path) ? Load(path) : new PresetFile();

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions) + Environment.NewLine);

    /// <summary>A file containing only <paramref name="name"/>, e.g. to share or to run with <c>--preset-file</c>.</summary>
    public PresetFile Export(string name) => new() { Presets = new(StringComparer.OrdinalIgnoreCase) { [name] = Presets[name] } };
}
