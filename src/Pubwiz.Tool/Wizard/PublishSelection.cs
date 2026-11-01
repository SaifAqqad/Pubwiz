using Pubwiz.Core.Discovery;
using Pubwiz.Core.Infrastructure.Git;
using Pubwiz.Core.Profiles;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// A project and the profile chosen for it: the output of the wizard.
/// </summary>
public sealed record PublishSelection(ProjectInfo Project, PublishProfile Profile);

/// <summary>
/// Options that change how the wizard behaves.
/// </summary>
/// <param name="Path">Folder, solution or project to start from.</param>
/// <param name="IncludeAll">Also list projects that normally can't be published (libraries, tests, ...).</param>
/// <param name="Confirm">Ask "Publish now?" before returning the selection.</param>
/// <param name="Git">The start folder's git state, shown above the project list.</param>
public sealed record WizardOptions(string Path, bool IncludeAll, bool Confirm, Task<GitInfo?>? Git = null);
