using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets.Maui;

namespace Pubwiz.Tool.Wizard;

/// <summary>A new profile, plus a password the page already knows (e.g. for a keystore it just created).</summary>
public sealed record CreatedProfile(PublishProfile Profile, string? Password = null);

/// <summary>
/// Settings for MAUI Android (.aab/.apk, keystore signing) and iOS (.ipa, macOS only).
/// </summary>
public sealed class MauiProfilePage(IAnsiConsole console, AndroidKeystore keystores, AppleSigning appleSigning)
{
    public async Task<CreatedProfile> CreateAsync(ProjectInfo project, MauiPlatform platform, CancellationToken cancellationToken)
    {
        var frameworks = platform.TargetFrameworks(project.TargetFrameworks);
        if (frameworks.Count == 0)
        {
            throw new UserErrorException($"{project.Name} doesn't target {platform} (no *{platform.TargetFrameworkSuffix()} target framework).");
        }

        var targetFramework = frameworks.Count == 1
            ? frameworks[0]
            : console.Choose("Target framework", frameworks);

        var profile = new PublishProfile($"{platform}Profile");
        profile[ProfileProperties.WebPublishMethod] = "Custom";
        profile[ProfileProperties.PublishProvider] = platform == MauiPlatform.Android ? ProfileProperties.MauiAndroidProvider : ProfileProperties.MauiIosProvider;
        profile[ProfileProperties.TargetFramework] = targetFramework;
        profile[ProfileProperties.LastUsedBuildConfiguration] = "Release";

        var password = platform == MauiPlatform.Android
            ? await AskAndroidSettingsAsync(project, profile, cancellationToken)
            : await AskIosSettingsAsync(profile, cancellationToken);

        AskVersion(profile);
        return new CreatedProfile(profile, password);
    }

    /// <returns>The keystore password when a new keystore was created here.</returns>
    private async Task<string?> AskAndroidSettingsAsync(ProjectInfo project, PublishProfile profile, CancellationToken cancellationToken)
    {
        profile[ProfileProperties.AndroidPackageFormats] = console.Choose("Package format", ["aab", "apk", "aab;apk"], f => f switch
        {
            "aab" => "AAB [dim](Google Play)[/]",
            "apk" => "APK [dim](install directly / ad-hoc)[/]",
            _ => "Both"
        });

        const string existing = "Use an existing keystore";
        const string create = "Create a new keystore";
        const string unsigned = "Don't sign [dim](debug key; can't be uploaded to Google Play)[/]";

        var signing = console.Choose("Signing", [existing, create, unsigned], choice => choice);

        if (signing == unsigned)
        {
            return null;
        }

        string? createdPassword = null;
        string keystorePath;
        string alias;

        if (signing == existing)
        {
            keystorePath = CommonPrompts.AskForFile(console, "Keystore file", ".keystore", ".jks");
            alias = console.Ask("Key alias");
        }
        else
        {
            keystorePath = Path.Combine(project.Directory, $"{project.Name.ToLowerInvariant()}.keystore");
            alias = console.Ask("Key alias", defaultValue: project.Name.ToLowerInvariant());
            var owner = console.Ask("Your name or organization", hint: "for the certificate");
            createdPassword = console.AskSecret("Keystore password", hint: "at least 6 characters", validate: p => p.Length >= 6 ? null : "At least 6 characters");

            await keystores.CreateAsync(keystorePath, alias, createdPassword, $"CN={owner}", cancellationToken);
            console.MarkupLine($"{Widgets.SuccessCheckMarkup} Created {keystorePath.EscapeMarkup()}");
            console.MarkupLine("[yellow]Back up this keystore and its password: updates on Google Play must be signed with the same key.[/]");
        }

        profile[ProfileProperties.AndroidKeyStore] = "true";
        profile[ProfileProperties.AndroidSigningKeyStore] = Path.GetRelativePath(project.Directory, keystorePath);
        profile[ProfileProperties.AndroidSigningKeyAlias] = alias;
        return createdPassword;
    }

    private async Task<string?> AskIosSettingsAsync(PublishProfile profile, CancellationToken cancellationToken)
    {
        var identities = await appleSigning.ListIdentitiesAsync(cancellationToken);
        profile[ProfileProperties.CodesignKey] = identities.Count > 0
            ? console.Choose("Signing identity", identities)
            : console.Ask("Signing identity", hint: "e.g. Apple Distribution: Contoso (TEAMID)");

        var provisioningProfiles = await appleSigning.ListProvisioningProfilesAsync(cancellationToken);
        profile[ProfileProperties.CodesignProvision] = provisioningProfiles.Count > 0
            ? console.Choose("Provisioning profile", provisioningProfiles, p => $"{p.Name.EscapeMarkup()} [dim]expires {p.ExpiresOn:yyyy-MM-dd}[/]").Name
            : console.Ask("Provisioning profile name");

        profile[ProfileProperties.RuntimeIdentifier] = "ios-arm64";
        profile[ProfileProperties.ArchiveOnBuild] = "true";
        return null;
    }

    private void AskVersion(PublishProfile profile)
    {
        var displayVersion = console.Ask("App version", hint: "optional, e.g. 1.2.0; empty = keep the project's", optional: true);
        var buildNumber = console.Ask("Build number", hint: "optional, e.g. 42", optional: true);
        profile[ProfileProperties.ApplicationDisplayVersion] = displayVersion.Length > 0 ? displayVersion : null;
        profile[ProfileProperties.ApplicationVersion] = buildNumber.Length > 0 ? buildNumber : null;
    }
}
