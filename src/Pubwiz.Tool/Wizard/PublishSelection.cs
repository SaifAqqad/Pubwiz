using Pubwiz.Core.Discovery;
using Pubwiz.Core.Presets;
using Pubwiz.Core.Publishing;
using Pubwiz.Tool.Rendering;
using Pubwiz.Core.Profiles;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// A project and the profile chosen for it: the output of the wizard.
/// </summary>
public sealed record PublishSelection(ProjectInfo Project, PublishProfile Profile)
{
    /// <summary>
    /// When it's published in a batch: lower numbers first, the same number at the same time
    /// (see <see cref="Core.Publishing.PublishOrder"/>). <c>null</c> when it wasn't chosen (e.g. <c>--project</c>, older presets).
    /// </summary>
    public int? Order { get; init; }
}

/// <summary>
/// Options that change how the wizard behaves.
/// </summary>
/// <param name="Path">Folder, solution or project to start from.</param>
/// <param name="IncludeAll">Also list projects that normally can't be published (libraries, tests, ...).</param>
/// <param name="Confirm">Ask "Publish now?" before returning the selection.</param>
/// <param name="Git">The start folder's git state, shown above the project list (and updated there after the fetch).</param>
/// <param name="Parallel">The most projects to publish at the same time, from <c>--parallel</c>; <c>null</c> for no limit.</param>
/// <param name="Recent">The last publish from here, offered at the top of the first list; <c>null</c> for none.</param>
/// <param name="Os">The OS to build for, from <c>--os</c>; <c>null</c> when it wasn't given.</param>
/// <param name="Architecture">The architecture to build for, from <c>--arch</c> (or <c>--os linux-arm64</c>); <c>null</c> when it wasn't given.</param>
public sealed record WizardOptions(string Path, bool IncludeAll, bool Confirm, GitStatus? Git = null, int? Parallel = null, RecentRun? Recent = null,
    PublishOs? Os = null, string? Architecture = null);
