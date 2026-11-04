using Pubwiz.Core.Discovery;
using Pubwiz.Core.Infrastructure.Git;
using Pubwiz.Core.Profiles;

namespace Pubwiz.Core.Publishing;

/// <summary>
/// What to publish: a project with one of its profiles.
/// </summary>
/// <param name="Project">The project to publish.</param>
/// <param name="Profile">The saved profile to publish with.</param>
public sealed record PublishRequest(ProjectInfo Project, PublishProfile Profile)
{
    /// <summary>
    /// The step of the batch this project is published in (see <see cref="PublishOrder"/>): step 1 first, and
    /// projects in the same step at the same time.
    /// </summary>
    public int Order { get; init; } = 1;

    /// <summary>Overrides the profile's build configuration when set.</summary>
    public string? Configuration { get; init; }

    /// <summary>Use detailed build output.</summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Whether to take an IIS app offline (<c>app_offline.htm</c>) while publishing, when set; <c>false</c> comes from
    /// <c>--no-app-offline</c>. When <c>null</c>, Web Deploy takes the app offline and FTP/SFTP follow their profile
    /// (their server may not be IIS).
    /// </summary>
    public bool? TakeAppOffline { get; init; }

    /// <summary>
    /// Only show what publishing would change on the server, without changing anything (<c>--what-if</c>). The app is
    /// still built. Only targets with <see cref="IPublishTarget.SupportsWhatIf"/> (Web Deploy) can do this.
    /// </summary>
    public bool WhatIf { get; init; }

    /// <summary>
    /// Overrides the profile's "allow an untrusted server certificate" setting (Web Deploy), when set, e.g. after the
    /// user chose to trust a self-signed certificate for this run.
    /// </summary>
    public bool? AllowUntrustedCertificate { get; init; }

    /// <summary>The deployment password (Web Deploy, FTP, ...), when the target needs one.</summary>
    public string? Password { get; init; }

    /// <summary>The branch and last commit of the project's repository, for reports; <c>null</c> outside a git repository.</summary>
    public GitInfo? Git { get; init; }

    /// <summary>The configuration to build: the override, then the profile's last used one, then Release.</summary>
    public string EffectiveConfiguration => Configuration ?? Profile[ProfileProperties.LastUsedBuildConfiguration] ?? "Release";
}
