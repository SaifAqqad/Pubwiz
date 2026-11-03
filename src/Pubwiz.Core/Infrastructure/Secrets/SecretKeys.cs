using Pubwiz.Core.Profiles;

namespace Pubwiz.Core.Infrastructure.Secrets;

/// <summary>
/// Builds the keys secrets are stored under.
/// </summary>
public static class SecretKeys
{
    /// <summary>The service name all Pubwiz secrets are grouped under in the OS store.</summary>
    public const string Service = "pubwiz";

    /// <summary>
    /// The key of a profile's deployment password: the profile file plus the user name, so two
    /// profiles (or two users on the same server) never share a password by accident.
    /// </summary>
    public static string ForProfile(PublishProfile profile)
    {
        var profilePath = profile.FilePath ?? throw new ArgumentException("The profile must be saved first.", nameof(profile));
        var userName = profile[ProfileProperties.UserName] ?? string.Empty;
        return $"profile:{Path.GetFullPath(profilePath)}:{userName}";
    }

    /// <summary>The key of a server's saved password (see <c>ServerCredentialStore</c>).</summary>
    /// <param name="server">The server's host in lower case, e.g. <c>10.0.0.5</c>.</param>
    public static string ForServer(string server) => $"server:{server}";

    /// <summary>A readable label, e.g. <c>Pubwiz: server 10.0.0.5 (deploy)</c>.</summary>
    public static string LabelForServer(string server, string userName) => $"Pubwiz: server {server} ({userName})";

    /// <summary>A readable label, e.g. <c>Pubwiz: Api / Production ($contoso)</c>.</summary>
    public static string LabelForProfile(string projectName, PublishProfile profile)
    {
        var userName = profile[ProfileProperties.UserName];
        return userName is null ? $"Pubwiz: {projectName} / {profile.Name}" : $"Pubwiz: {projectName} / {profile.Name} ({userName})";
    }
}
