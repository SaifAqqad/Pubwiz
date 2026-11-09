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

    /// <summary>
    /// The OS to build for (<c>--os</c>, a preset, or the confirm page), used when neither the profile nor the target
    /// decides it. See <see cref="PublishPlatform"/>.
    /// </summary>
    public PublishOs? Os { get; init; }

    /// <summary>
    /// The architecture to build for, e.g. <c>arm64</c> (<c>--arch</c>, a preset, or the confirm page), used unless the
    /// profile has a runtime. See <see cref="PublishPlatform"/>.
    /// </summary>
    public string? Architecture { get; init; }

    /// <summary>What the target says about the platform; set by <see cref="PublishRunner.CreateJob"/>.</summary>
    public TargetPlatform TargetPlatform { get; init; } = TargetPlatform.Any;

    /// <summary>The platform to build for, and the runtime identifier passed to <c>dotnet publish</c>.</summary>
    public ResolvedPlatform Platform => PublishPlatform.Resolve(Project, Profile, TargetPlatform, Os, Architecture);

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

    /// <summary>
    /// The deployment password (Web Deploy, FTP, ...), when the target needs one. Azure App Service profiles without
    /// one sign in with az instead, e.g. for apps that have basic authentication turned off.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>The branch and last commit of the project's repository, for reports; <c>null</c> outside a git repository.</summary>
    public GitInfo? Git { get; init; }

    /// <summary>
    /// The configuration to build: the override, then the profile's (<c>LastUsedBuildConfiguration</c>, or
    /// <c>Configuration</c> in Visual Studio folder profiles of non-web projects), then Release.
    /// </summary>
    public string EffectiveConfiguration => Configuration
        ?? Profile[ProfileProperties.LastUsedBuildConfiguration]
        ?? Profile[ProfileProperties.Configuration]
        ?? "Release";
}
