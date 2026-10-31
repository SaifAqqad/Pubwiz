namespace Pubwiz.Core.Discovery;

/// <summary>
/// Finds the solutions or projects to work with, starting from a folder or file.
/// </summary>
/// <remarks>
/// The search goes level by level, up to <see cref="MaxDepth"/> folders below the start folder.
/// At each level, solutions win over projects, and the first level with any match ends the search.
/// </remarks>
public static class WorkspaceScanner
{
    /// <summary>
    /// How many folder levels below the start folder are searched.
    /// </summary>
    public const int MaxDepth = 2;

    private static readonly string[] SolutionExtensions = [".sln", ".slnx"];

    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];

    /// <summary>
    /// Folders that never contain projects worth publishing.
    /// </summary>
    private static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", "packages", "artifacts", "dist", "TestResults"
    };

    /// <summary>
    /// Scans <paramref name="path"/>, which can be a folder, a solution file or a project file.
    /// </summary>
    public static WorkspaceScanResult Scan(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (File.Exists(fullPath))
        {
            return ScanFile(fullPath);
        }

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"'{path}' does not exist.");
        }

        IReadOnlyList<string> currentLevel = [fullPath];
        for (var depth = 0; depth <= MaxDepth && currentLevel.Count > 0; depth++)
        {
            var solutions = FindFiles(currentLevel, SolutionExtensions);
            if (solutions.Count > 0)
            {
                return new WorkspaceScanResult(solutions, []);
            }

            var projects = FindFiles(currentLevel, ProjectExtensions);
            if (projects.Count > 0)
            {
                return new WorkspaceScanResult([], projects);
            }

            currentLevel = GetSubfolders(currentLevel);
        }

        return WorkspaceScanResult.Empty;
    }

    private static WorkspaceScanResult ScanFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);

        if (SolutionExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return new WorkspaceScanResult([filePath], []);
        }

        if (ProjectExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return new WorkspaceScanResult([], [filePath]);
        }

        throw new ArgumentException($"'{filePath}' is not a solution or project file.", nameof(filePath));
    }

    private static List<string> FindFiles(IEnumerable<string> folders, string[] extensions)
    {
        return folders
            .SelectMany(folder => Directory.EnumerateFiles(folder))
            .Where(file => extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> GetSubfolders(IEnumerable<string> folders)
    {
        return folders
            .SelectMany(folder => Directory.EnumerateDirectories(folder))
            .Where(folder => !IsIgnored(folder))
            .ToList();
    }

    private static bool IsIgnored(string folder)
    {
        var name = Path.GetFileName(folder);
        return name.StartsWith('.') || IgnoredFolders.Contains(name);
    }
}
