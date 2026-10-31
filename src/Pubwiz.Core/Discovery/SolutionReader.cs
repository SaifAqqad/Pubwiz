using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace Pubwiz.Core.Discovery;

/// <summary>
/// A project listed in a solution.
/// </summary>
/// <param name="FilePath">Full path of the project file.</param>
/// <param name="SolutionFolder">The solution folder it sits in, e.g. <c>/src/</c>, or <c>null</c> at the root.</param>
public sealed record SolutionProject(string FilePath, string? SolutionFolder);

/// <summary>
/// Reads the projects of a .sln or .slnx file, using the same library as the dotnet CLI.
/// </summary>
public static class SolutionReader
{
    public static async Task<IReadOnlyList<SolutionProject>> ReadProjectsAsync(string solutionPath, CancellationToken cancellationToken = default)
    {
        var serializer = SolutionSerializers.GetSerializerByMoniker(solutionPath)
            ?? throw new NotSupportedException($"'{Path.GetFileName(solutionPath)}' is not a supported solution format.");

        var solution = await serializer.OpenAsync(solutionPath, cancellationToken);
        var solutionFolder = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;

        return solution.SolutionProjects
            .Select(project => new SolutionProject(
                FilePath: ToFullPath(solutionFolder, project.FilePath),
                SolutionFolder: project.Parent?.Path))
            .Where(project => File.Exists(project.FilePath))
            .ToList();
    }

    /// <summary>
    /// Solution files always use backslashes, so they are normalized for the current OS.
    /// </summary>
    private static string ToFullPath(string solutionFolder, string relativePath)
    {
        var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(solutionFolder, normalized));
    }
}
