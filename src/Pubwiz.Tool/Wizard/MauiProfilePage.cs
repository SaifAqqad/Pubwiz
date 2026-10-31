using Pubwiz.Core.Discovery;
using Pubwiz.Core.Maui;
using Pubwiz.Core.Profiles;

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
            : console.Prompt(new SelectionPrompt<string>().Title("Target framework:").HighlightStyle(new Style(Widgets.PrimaryColor)).AddChoices(frameworks));

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
        profile[ProfileProperties.AndroidPackageFormats] = console.Prompt(new SelectionPrompt<string>()
            .Title("Package format:")
            .HighlightStyle(new Style(Widgets.PrimaryColor))
            .AddChoices("aab", "apk", "aab;apk")
            .UseConverter(f => f switch
            {
                "aab" => "AAB [dim](Google Play)[/]",
                "apk" => "APK [dim](install directly / ad-hoc)[/]",
                _ => "Both"
            }));

        const string existing = "Use an existing keystore";
        const string create = "Create a new keystore";
        const string unsigned = "Don't sign [dim](debug key; can't be uploaded to Google Play)[/]";

        var signing = console.Prompt(new SelectionPrompt<string>()
            .Title("Signing:")
            .HighlightStyle(new Style(Widgets.PrimaryColor))
            .AddChoices(existing, create, unsigned));

        if (signing == unsigned)
        {
            return null;
        }

        string? createdPassword = null;
        string keystorePath;
        string alias;

        if (signing == existing)
        {
            keystorePath = CommonPrompts.AskForFile(console, "Keystore file:", ".keystore", ".jks");
            alias = console.Prompt(new TextPrompt<string>("Key alias:"));
        }
        else
        {
            keystorePath = Path.Combine(project.Directory, $"{project.Name.ToLowerInvariant()}.keystore");
            alias = console.Prompt(new TextPrompt<string>("Key alias:").DefaultValue(project.Name.ToLowerInvariant()));
            var owner = console.Prompt(new TextPrompt<string>("Your name or organization [dim](for the certificate)[/]:"));
            createdPassword = console.Prompt(new TextPrompt<string>("Keystore password [dim](at least 6 characters)[/]:")
                .Secret()
                .Validate(p => p.Length >= 6 ? ValidationResult.Success() : ValidationResult.Error("[red]At least 6 characters[/]")));

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
            ? console.Prompt(new SelectionPrompt<string>().Title("Signing identity:").HighlightStyle(new Style(Widgets.PrimaryColor)).AddChoices(identities))
            : console.Prompt(new TextPrompt<string>("Signing identity [dim](e.g. Apple Distribution: Contoso (TEAMID))[/]:"));

        var provisioningProfiles = await appleSigning.ListProvisioningProfilesAsync(cancellationToken);
        profile[ProfileProperties.CodesignProvision] = provisioningProfiles.Count > 0
            ? console.Prompt(new SelectionPrompt<ProvisioningProfile>()
                .Title("Provisioning profile:")
                .HighlightStyle(new Style(Widgets.PrimaryColor))
                .AddChoices(provisioningProfiles)
                .UseConverter(p => $"{p.Name.EscapeMarkup()} [dim]expires {p.ExpiresOn:yyyy-MM-dd}[/]")).Name
            : console.Prompt(new TextPrompt<string>("Provisioning profile name:"));

        profile[ProfileProperties.RuntimeIdentifier] = "ios-arm64";
        profile[ProfileProperties.ArchiveOnBuild] = "true";
        return null;
    }

    private void AskVersion(PublishProfile profile)
    {
        var displayVersion = console.Prompt(new TextPrompt<string>("App version [dim](optional, e.g. 1.2.0; empty = keep the project's)[/]:").AllowEmpty());
        var buildNumber = console.Prompt(new TextPrompt<string>("Build number [dim](optional, e.g. 42)[/]:").AllowEmpty());
        profile[ProfileProperties.ApplicationDisplayVersion] = displayVersion.Length > 0 ? displayVersion : null;
        profile[ProfileProperties.ApplicationVersion] = buildNumber.Length > 0 ? buildNumber : null;
    }
}
