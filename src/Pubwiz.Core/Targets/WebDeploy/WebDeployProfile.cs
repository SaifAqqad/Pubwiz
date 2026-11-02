using Pubwiz.Core.Infrastructure.Platform;
using Pubwiz.Core.Profiles;
using Pubwiz.WebDeploy;

namespace Pubwiz.Core.Targets.WebDeploy;

/// <summary>
/// Reads Web Deploy settings from a publish profile.
/// </summary>
public static class WebDeployProfile
{
    /// <summary>The endpoint from <c>MSDeployServiceURL</c> and <c>DeployIisAppPath</c>.</summary>
    /// <exception cref="ArgumentException">The profile has no service URL or site.</exception>
    public static WebDeployEndpoint GetEndpoint(PublishProfile profile)
    {
        var serviceUrl = profile[ProfileProperties.MSDeployServiceURL];
        var iisPath = profile[ProfileProperties.DeployIisAppPath];

        if (string.IsNullOrWhiteSpace(serviceUrl) || string.IsNullOrWhiteSpace(iisPath))
        {
            throw new ArgumentException($"The profile '{profile.Name}' needs both {ProfileProperties.MSDeployServiceURL} and {ProfileProperties.DeployIisAppPath}.", nameof(profile));
        }

        return WebDeployEndpoint.Create(serviceUrl, iisPath);
    }

    /// <summary>What to do when the server's certificate isn't trusted, added to that error.</summary>
    public const string UntrustedCertificateHint =
        $"Publish again and choose to trust it, or set {ProfileProperties.AllowUntrustedCertificate} to true in the profile.";

    /// <summary>Whether the profile accepts an untrusted (e.g. self-signed) server certificate.</summary>
    public static bool AllowsUntrustedCertificate(PublishProfile profile) =>
        string.Equals(profile[ProfileProperties.AllowUntrustedCertificate], "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether to compare files by checksum (<c>MSDeployUseChecksum</c>).</summary>
    public static bool UsesChecksum(PublishProfile profile) =>
        string.Equals(profile[ProfileProperties.MSDeployUseChecksum], "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The profile's skip rules: its <c>MsDeploySkipRules</c> items, as Visual Studio writes them:
    /// <code>
    /// &lt;MsDeploySkipRules Include="KeepUploads"&gt;
    ///   &lt;ObjectName&gt;dirPath&lt;/ObjectName&gt;
    ///   &lt;AbsolutePath&gt;wwwroot\\uploads$&lt;/AbsolutePath&gt;
    ///   &lt;SkipAction&gt;Delete&lt;/SkipAction&gt;   (optional: only never delete)
    /// &lt;/MsDeploySkipRules&gt;
    /// </code>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A rule can't be applied. It's an error rather than being ignored, because an ignored "keep these files" rule could
    /// let a sync delete them.
    /// </exception>
    public static IReadOnlyList<WebDeploySkipRule> GetSkipRules(PublishProfile profile)
    {
        var rules = new List<WebDeploySkipRule>();
        foreach (var item in profile.Items.Where(i => string.Equals(i.Type, ProfileProperties.MsDeploySkipRules, StringComparison.OrdinalIgnoreCase)))
        {
            var name = $"The skip rule '{item.Include}' in the profile '{profile.Name}'";

            var target = item["ObjectName"]?.ToLowerInvariant() switch
            {
                "filepath" => WebDeploySkipObject.File,
                "dirpath" => WebDeploySkipObject.Directory,
                _ => throw new ArgumentException($"{name} needs an ObjectName of filePath or dirPath (Pubwiz doesn't support '{item["ObjectName"]}').", nameof(profile))
            };

            var path = item["AbsolutePath"]
                       ?? throw new ArgumentException($"{name} needs an AbsolutePath (Pubwiz doesn't support rules by KeyAttribute).", nameof(profile));

            WebDeploySkipAction? action = item["SkipAction"] is not { } skipAction ? null
                : Enum.TryParse<WebDeploySkipAction>(skipAction, ignoreCase: true, out var parsed) ? parsed
                : throw new ArgumentException($"{name} has the SkipAction '{skipAction}'; Pubwiz supports Delete, Update and AddChild.", nameof(profile));

            if (WebDeploySkipRule.GetPathError(path) is { } pathError)
            {
                throw new ArgumentException($"{name}: {pathError}", nameof(profile));
            }

            rules.Add(new WebDeploySkipRule(target, path) { Action = action });
        }

        return rules;
    }

    /// <summary>
    /// Whether the built-in cross-platform client can publish this profile. It authenticates with HTTP Basic
    /// (a user name and password) only, not NTLM / the current Windows user, so it needs a user name.
    /// </summary>
    public static bool CanUseBuiltInClient(PublishProfile profile) =>
        !string.IsNullOrWhiteSpace(profile[ProfileProperties.UserName])
        && !string.Equals(profile[ProfileProperties.AuthType], "NTLM", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Why the built-in client can't publish this profile, or <c>null</c> when it can. Used when msdeploy.exe
    /// isn't available (Linux, macOS, or Windows without Web Deploy installed).
    /// </summary>
    public static string? BuiltInClientBlocker(PublishProfile profile) =>
        CanUseBuiltInClient(profile)
            ? null
            : OperatingSystemInfo.IsWindows
                ? $"Web Deploy isn't installed, and the built-in client can't sign in as the current Windows user. Install Web Deploy ({MsDeployLocator.DownloadUrl}), or set a user name and password on the profile."
                : "This profile signs in as the current Windows user (NTLM), which only works with Web Deploy on Windows. Set a user name and password on the profile to publish from here.";
}
