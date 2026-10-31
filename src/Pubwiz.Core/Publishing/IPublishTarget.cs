using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets;

namespace Pubwiz.Core.Publishing;

/// <summary>
/// A place to publish to (folder, IIS, Azure, ...). Each target turns a request into the steps that publish it.
/// </summary>
public interface IPublishTarget
{
    PublishTargetType Type { get; }

    /// <summary>Whether this target publishes profiles like <paramref name="profile"/>.</summary>
    bool CanPublish(PublishProfile profile);

    /// <summary>Whether publishing needs a password (asked for or read from the secret store before running).</summary>
    bool RequiresPassword(PublishProfile profile);

    /// <summary>
    /// Why <paramref name="profile"/> can't be published from this machine (a missing tool, an incomplete profile),
    /// or <c>null</c> when it can. Checked before anything runs, so the job fails straight away.
    /// </summary>
    string? GetSetupError(PublishProfile profile) => null;

    /// <summary>Creates fresh steps for one run.</summary>
    IReadOnlyList<PublishStep> CreateSteps(PublishRequest request);
}
