using Pubwiz.Core.Processes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Pubwiz.Core.Maui;

/// <summary>An installed provisioning profile.</summary>
/// <param name="Name">What <c>CodesignProvision</c> expects.</param>
/// <param name="ExpiresOn">When it stops working.</param>
public sealed record ProvisioningProfile(string Name, DateTimeOffset? ExpiresOn);

/// <summary>
/// Finds iOS signing identities and provisioning profiles on macOS, using the built-in <c>security</c> tool.
/// </summary>
public sealed partial class AppleSigning(IProcessRunner processRunner)
{
    private const string SecurityTool = "/usr/bin/security";

    private static readonly string ProvisioningProfilesFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "MobileDevice", "Provisioning Profiles");

    /// <summary>Code signing identities in the keychain, e.g. <c>Apple Distribution: Contoso (TEAMID)</c>.</summary>
    public async Task<IReadOnlyList<string>> ListIdentitiesAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SecurityTool))
        {
            return [];
        }

        var result = await processRunner.RunAsync(new ProcessSpec(SecurityTool, ["find-identity", "-v", "-p", "codesigning"]), cancellationToken: cancellationToken);
        return result.Succeeded ? ParseIdentities(result.Output) : [];
    }

    /// <summary>Installed provisioning profiles that haven't expired, newest expiry first.</summary>
    public async Task<IReadOnlyList<ProvisioningProfile>> ListProvisioningProfilesAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SecurityTool) || !Directory.Exists(ProvisioningProfilesFolder))
        {
            return [];
        }

        var profiles = new List<ProvisioningProfile>();
        foreach (var file in Directory.EnumerateFiles(ProvisioningProfilesFolder, "*.mobileprovision"))
        {
            // .mobileprovision files are signed (CMS) plists; "security cms -D" prints the plist.
            var result = await processRunner.RunAsync(new ProcessSpec(SecurityTool, ["cms", "-D", "-i", file]), cancellationToken: cancellationToken);
            if (!result.Succeeded || ParseProvisioningProfile(result.Output) is not { } profile)
            {
                continue;
            }

            var expired = profile.ExpiresOn is { } expiry && expiry <= DateTimeOffset.Now;
            if (!expired)
            {
                profiles.Add(profile);
            }
        }

        return profiles.OrderByDescending(p => p.ExpiresOn).ToList();
    }

    /// <summary>Lines look like <c>  1) 0A1B2C... "Apple Distribution: Contoso (TEAMID)"</c>.</summary>
    internal static IReadOnlyList<string> ParseIdentities(string output) =>
        IdentityLine().Matches(output).Select(m => m.Groups["name"].Value).Distinct().ToList();

    /// <summary>Reads <c>Name</c> and <c>ExpirationDate</c> from a provisioning profile plist.</summary>
    internal static ProvisioningProfile? ParseProvisioningProfile(string plist)
    {
        try
        {
            var dictionary = XDocument.Parse(plist).Root?.Element("dict");
            var elements = dictionary?.Elements().ToList() ?? [];

            string? ValueOf(string key) => elements.SkipWhile(e => !(e.Name == "key" && e.Value == key)).Skip(1).FirstOrDefault()?.Value;

            var name = ValueOf("Name");
            if (name is null)
            {
                return null;
            }

            var expires = DateTimeOffset.TryParse(ValueOf("ExpirationDate"), out var date) ? date : (DateTimeOffset?)null;
            return new ProvisioningProfile(name, expires);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    [GeneratedRegex("""^\s*\d+\)\s+[0-9A-F]{40}\s+"(?<name>[^"]+)"\r?$""", RegexOptions.Multiline)]
    private static partial Regex IdentityLine();
}
