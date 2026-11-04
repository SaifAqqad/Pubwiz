using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Picks projects and a profile from command line arguments (<c>--project</c>, <c>--profile</c>), without prompts.
/// </summary>
public sealed class ArgumentSelection(WorkspacePage workspacePage)
{
    /// <param name="path">Where to look for projects.</param>
    /// <param name="projectNames">
    /// Project names (e.g. <c>Api</c>) or paths to project files, each optionally followed by <c>:order</c>
    /// (e.g. <c>Api:1</c>, see <see cref="ParseProject"/>).
    /// </param>
    /// <param name="profileName">The profile every selected project is published with.</param>
    /// <param name="cancellationToken">Cancels loading.</param>
    /// <exception cref="UserErrorException">A project or profile wasn't found.</exception>
    public async Task<PublishBatch> SelectAsync(string path, IReadOnlyList<string> projectNames, string? profileName, CancellationToken cancellationToken)
    {
        if (profileName is null)
        {
            throw new UserErrorException("--project needs --profile to say which publish profile to use.");
        }

        var workspace = await workspacePage.LoadAsync(path, includeAll: true, cancellationToken);
        var selections = new List<PublishSelection>();

        foreach (var argument in projectNames)
        {
            var (name, order) = ParseProject(argument);
            var project = FindProject(workspace, name)
                ?? throw new UserErrorException($"No project named '{name}' was found. Projects: {string.Join(", ", workspace.Projects.Select(p => p.Name))}.");

            var profile = PublishProfileStore.List(project.Directory).FirstOrDefault(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase))
                ?? throw new UserErrorException($"{project.Name} has no publish profile named '{profileName}'.");

            selections.Add(new PublishSelection(project, profile) { Order = order });
        }

        return new PublishBatch(selections);
    }

    /// <summary>
    /// Splits a <c>--project</c> value into the project and its order: <c>Api:2</c> is Api in step 2. Only digits after
    /// the last colon count as an order, so Windows paths such as <c>C:\src\Api.csproj</c> are left alone.
    /// </summary>
    /// <exception cref="UserErrorException">The order is 0.</exception>
    internal static (string Project, int? Order) ParseProject(string value)
    {
        var colon = value.LastIndexOf(':');
        if (colon <= 0 || colon == value.Length - 1 || !value[(colon + 1)..].All(char.IsAsciiDigit))
        {
            return (value, null);
        }

        return int.TryParse(value[(colon + 1)..], out var order) && order >= 1
            ? (value[..colon], order)
            : throw new UserErrorException($"The order in '--project {value}' must be a number from 1 up.");
    }

    /// <summary>Matches by project name (case-insensitive) or by project file path.</summary>
    internal static ProjectInfo? FindProject(Workspace workspace, string nameOrPath)
    {
        var byName = workspace.Projects.FirstOrDefault(p => string.Equals(p.Name, nameOrPath, StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
        {
            return byName;
        }

        var fullPath = Path.GetFullPath(nameOrPath);
        return workspace.Projects.FirstOrDefault(p => string.Equals(p.FilePath, fullPath, StringComparison.Ordinal));
    }
}
