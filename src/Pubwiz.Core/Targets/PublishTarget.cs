namespace Pubwiz.Core.Targets;

/// <summary>
/// Groups targets in the wizard, like Visual Studio's publish dialog. Listed in this order.
/// </summary>
public enum PublishTargetGroup
{
    /// <summary>Output on this machine: a folder, a zip or a package.</summary>
    Local,

    /// <summary>Your own servers: IIS, FTP, SFTP.</summary>
    Servers,

    Azure,

    /// <summary>Amazon Web Services.</summary>
    Aws,

    Containers,
    Mobile
}

/// <summary>
/// Describes a publish target shown in the "create profile" step.
/// </summary>
/// <param name="Type">Which target this is.</param>
/// <param name="Group">The heading it's listed under.</param>
/// <param name="DisplayName">Short name, e.g. "Folder".</param>
/// <param name="Description">One line describing what it does.</param>
public sealed record PublishTarget(PublishTargetType Type, PublishTargetGroup Group, string DisplayName, string Description)
{
    /// <summary>
    /// Why the target can't be used on this machine or in this version, or <c>null</c> when it can.
    /// </summary>
    public string? UnavailableReason { get; init; }

    public bool IsAvailable => UnavailableReason is null;

    /// <summary>
    /// A caveat for a target that can still be chosen, e.g. Web Deploy on Linux: the profile can be created
    /// (for a Windows machine or CI), but won't publish from here. <c>null</c> when there's none.
    /// </summary>
    public string? Warning { get; init; }
}
