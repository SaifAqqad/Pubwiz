using Pubwiz.Core.Profiles;

namespace Pubwiz.Core.Publishing;

/// <summary>
/// Keeps the builds of jobs that run at the same time apart.
/// </summary>
/// <remarks>
/// Projects in a batch often reference the same library. Built side by side, every <c>dotnet publish</c> would write
/// that library's bin and obj folders at the same time, and the builds fail on locked or half-written files
/// (the same goes for one project published with two profiles). So in parallel runs each job builds into its own
/// folder (<c>--artifacts-path</c>), which holds the bin and obj folders of its project and of every project it references.
/// The folder is kept between runs, so later builds are incremental.
/// </remarks>
public static class BuildIsolation
{
    /// <summary><c>true</c> when the jobs run at the same time and so need separate build folders.</summary>
    /// <param name="jobCount">How many jobs run in the biggest step of the batch (only jobs of the same step run together).</param>
    /// <param name="maxParallelism">How many of them run at the same time.</param>
    public static bool IsNeeded(int jobCount, int maxParallelism) => jobCount > 1 && maxParallelism > 1;

    /// <summary>
    /// The job's build folder: <c>obj/pubwiz-build/&lt;profile&gt;/</c> inside the project. It's next to the staging
    /// folders (<c>obj/pubwiz/</c>) rather than inside them, because publishing empties the staging folder first.
    /// </summary>
    public static string GetBuildDirectory(PublishRequest request) =>
        Path.Combine(request.Project.Directory, "obj", "pubwiz-build", PublishProfileStore.ToValidName(request.Profile.Name));
}
