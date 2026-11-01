namespace Pubwiz.Core.Discovery;

/// <summary>
/// Finds the solutions or projects to work with, starting from a folder or file.
/// </summary>
/// <remarks>
/// The search goes in this order:
/// <list type="number">
///   <item>solutions in the start folder;</item>
///   <item>solutions in the parent folders (up to <see cref="MaxParentDepth"/> levels, never above the git repository root,
///   the home folder or the drive root) that include a project inside the start folder;</item>
///   <item>the start folder and its subfolders, level by level up to <see cref="MaxDepth"/> levels down: at each level
///   solutions win over projects, and the first level with any match ends the search;</item>
///   <item>the nearest parent solution inside the same git repository, even when it doesn't include the start folder
///   (e.g. when Pubwiz is started from a <c>docs</c> folder).</item>
/// </list>
/// </remarks>
public static class WorkspaceScanner
{
    /// <summary>
    /// How many folder levels below the start folder are searched.
    /// </summary>
    public const int MaxDepth = 2;

    /// <summary>
    /// How many parent folders are searched for a solution.
    /// </summary>
    public const int MaxParentDepth = 5;

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
    public static async Task<WorkspaceScanResult> ScanAsync(string path, CancellationToken cancellationToken = default)
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

        var solutionsHere = FindFiles([fullPath], SolutionExtensions);
        if (solutionsHere.Count > 0)
        {
            return new WorkspaceScanResult(solutionsHere, []);
        }

        var (including, nearestInRepository) = await FindParentSolutionsAsync(fullPath, cancellationToken);
        if (including.Count > 0)
        {
            return new WorkspaceScanResult(including, []);
        }

        var below = ScanDown(fullPath);
        if (!below.IsEmpty)
        {
            return below;
        }

        return nearestInRepository.Count > 0 ? new WorkspaceScanResult(nearestInRepository, []) : WorkspaceScanResult.Empty;
    }

    /// <summary>
    /// Walks up from <paramref name="startFolder"/> looking for solutions.
    /// </summary>
    /// <returns>
    /// The solutions of the nearest level that include a project inside the start folder, and (as a fallback)
    /// the nearest level's solutions inside the same git repository.
    /// </returns>
    private static async Task<(List<string> Including, List<string> NearestInRepository)> FindParentSolutionsAsync(string startFolder,
        CancellationToken cancellationToken)
    {
        var parents = GetParentsToSearch(startFolder, out var repositoryRoot);
        List<string> nearest = [];

        foreach (var folder in parents)
        {
            var solutions = FindFiles([folder], SolutionExtensions);
            if (solutions.Count == 0)
            {
                continue;
            }

            var including = new List<string>();
            foreach (var solution in solutions)
            {
                if (await IncludesFolderAsync(solution, startFolder, cancellationToken))
                {
                    including.Add(solution);
                }
            }

            if (including.Count > 0)
            {
                return (including, []);
            }

            if (nearest.Count == 0 && repositoryRoot is not null)
            {
                nearest = solutions;
            }
        }

        return ([], nearest);
    }

    /// <summary>
    /// The parent folders to search, nearest first. The walk stops at the git repository root (included), and never
    /// reaches the home folder or the drive root, where a solution is very unlikely to belong to the start folder.
    /// </summary>
    /// <param name="startFolder">The full path of the start folder.</param>
    /// <param name="repositoryRoot">The git repository root, when the start folder is inside one within reach.</param>
    private static List<string> GetParentsToSearch(string startFolder, out string? repositoryRoot)
    {
        repositoryRoot = null;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var parents = new List<string>();

        if (IsRepositoryRoot(startFolder))
        {
            // A solution above the repository belongs to another one.
            repositoryRoot = startFolder;
            return parents;
        }

        var current = Directory.GetParent(startFolder);
        while (current is { Parent: not null } && parents.Count < MaxParentDepth && !SamePath(current.FullName, home))
        {
            parents.Add(current.FullName);
            if (IsRepositoryRoot(current.FullName))
            {
                repositoryRoot = current.FullName;
                break;
            }

            current = current.Parent;
        }

        return parents;
    }

    /// <summary><c>true</c> when one of the solution's projects is inside <paramref name="folder"/>.</summary>
    private static async Task<bool> IncludesFolderAsync(string solutionPath, string folder, CancellationToken cancellationToken)
    {
        try
        {
            var projects = await SolutionReader.ReadProjectsAsync(solutionPath, cancellationToken);
            var prefix = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
            return projects.Any(project => project.FilePath.StartsWith(prefix, PathComparison));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An unreadable solution can't be checked, so it's not used.
            return false;
        }
    }

    /// <summary>The original search: the start folder, then its subfolders level by level.</summary>
    private static WorkspaceScanResult ScanDown(string startFolder)
    {
        IReadOnlyList<string> currentLevel = [startFolder];
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

    /// <summary>A <c>.git</c> folder, or a <c>.git</c> file for worktrees and submodules.</summary>
    private static bool IsRepositoryRoot(string folder)
    {
        var git = Path.Combine(folder, ".git");
        return Directory.Exists(git) || File.Exists(git);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), PathComparison);
}
