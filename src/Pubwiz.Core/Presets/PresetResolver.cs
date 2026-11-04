using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;

namespace Pubwiz.Core.Presets;

/// <summary>A preset entry that can't be used, and why.</summary>
public sealed record PresetProblem(PresetProject Entry, string Reason);

/// <summary>The projects and profiles of a preset, ready to publish.</summary>
public sealed record ResolvedPreset(IReadOnlyList<(ProjectInfo Project, PublishProfile Profile, int? Order)> Items, IReadOnlyList<PresetProblem> Problems)
{
    public bool IsValid => Problems.Count == 0;
}

/// <summary>
/// Turns preset entries (relative project paths + profile names) into loaded projects and profiles.
/// </summary>
public sealed class PresetResolver(ProjectInspector inspector)
{
    /// <param name="preset">The preset to resolve.</param>
    /// <param name="baseDirectory">The folder the preset's paths are relative to (the preset file's folder).</param>
    /// <param name="cancellationToken">Cancels project evaluation.</param>
    public async Task<ResolvedPreset> ResolveAsync(PublishPreset preset, string baseDirectory, CancellationToken cancellationToken = default)
    {
        var problems = new List<PresetProblem>();
        var existing = new List<(PresetProject Entry, string FullPath)>();

        foreach (var entry in preset.Projects)
        {
            var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, entry.Path.Replace('\\', '/')));
            if (entry.Order is < 1)
            {
                problems.Add(new PresetProblem(entry, $"the order must be 1 or more, not {entry.Order}"));
            }
            else if (File.Exists(fullPath))
            {
                existing.Add((entry, fullPath));
            }
            else
            {
                problems.Add(new PresetProblem(entry, $"project not found: {fullPath}"));
            }
        }

        var projects = await inspector.InspectAsync(existing.Select(e => new SolutionProject(e.FullPath, SolutionFolder: null)).ToList(), cancellationToken);

        var items = new List<(ProjectInfo, PublishProfile, int?)>();
        for (var i = 0; i < existing.Count; i++)
        {
            var (entry, _) = existing[i];
            var project = projects[i];
            var profile = PublishProfileStore.List(project.Directory).FirstOrDefault(p => string.Equals(p.Name, entry.Profile, StringComparison.OrdinalIgnoreCase));

            if (project.EvaluationError is not null)
            {
                problems.Add(new PresetProblem(entry, $"project could not be loaded: {project.EvaluationError}"));
            }
            else if (profile is null)
            {
                problems.Add(new PresetProblem(entry, $"{project.Name} has no publish profile named '{entry.Profile}'"));
            }
            else
            {
                items.Add((project, profile, entry.Order));
            }
        }

        return new ResolvedPreset(items, problems);
    }

    /// <summary>Builds a preset entry for a project, with its path relative to <paramref name="baseDirectory"/> using forward slashes.</summary>
    public static PresetProject CreateEntry(ProjectInfo project, PublishProfile profile, string baseDirectory, int? order = null) =>
        new(Path.GetRelativePath(baseDirectory, project.FilePath).Replace('\\', '/'), profile.Name, order);
}
