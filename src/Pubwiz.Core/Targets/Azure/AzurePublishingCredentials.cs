using Pubwiz.Core.Profiles;

namespace Pubwiz.Core.Targets.Azure;

/// <summary>
/// Gets the publishing password of an existing Azure App Service profile (Zip Deploy, or Web Deploy to an App Service)
/// from Azure, with the signed-in <c>az</c>, so it doesn't have to be typed or stored.
/// </summary>
/// <remarks>
/// The app is found from the profile's resource ID (Visual Studio profiles), the Pubwiz Azure properties, or else by
/// matching its SCM host against the apps in the active subscription (e.g. imported .publishsettings).
/// Only a password for the profile's own user name is used, so user-level deployment credentials are still asked for.
/// </remarks>
public sealed class AzurePublishingCredentials(AzCli az)
{
    private Task<bool>? _signedIn;

    /// <summary><c>true</c> for Azure App Service profiles that sign in with a publishing user name and password.</summary>
    public static bool Applies(PublishProfile profile) => AzureProfile.IsAppService(profile) && !AzureProfile.UsesAzCli(profile);

    /// <returns>
    /// The password and the app it came from, or <c>null</c> when it can't be fetched (not an Azure profile, az missing
    /// or signed out, app not found, ...). Errors never throw: the caller falls back to asking.
    /// </returns>
    public async Task<(string Password, string AppName)?> TryGetAsync(PublishProfile profile, CancellationToken cancellationToken)
    {
        if (!Applies(profile) || !az.IsInstalled)
        {
            return null;
        }

        try
        {
            if (!await (_signedIn ??= IsSignedInAsync(cancellationToken)))
            {
                return null;
            }

            if (await FindAppAsync(profile, cancellationToken) is not { } app)
            {
                return null;
            }

            var xml = await az.GetPublishingProfilesXmlAsync(app, cancellationToken);
            return PickPassword(PublishSettingsImporter.ReadXml(xml), profile[ProfileProperties.UserName]) is { } password
                ? (password, app.Slot is null ? app.Name : $"{app.Name}/{app.Slot}")
                : null;
        }
        catch (Exception ex) when (ex is AzCliException or System.Xml.XmlException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The password of the Web Deploy or Zip Deploy entry for <paramref name="userName"/> (the same credentials).</summary>
    internal static string? PickPassword(IReadOnlyList<PublishSettingsEntry> entries, string? userName) => entries
        .Where(e => e.Method is PublishMethod.MSDeploy or PublishMethod.ZipDeploy)
        .FirstOrDefault(e => e.Password is { Length: > 0 } && string.Equals(e.UserName, userName, StringComparison.OrdinalIgnoreCase))
        ?.Password;

    private async Task<bool> IsSignedInAsync(CancellationToken cancellationToken) => await az.GetCurrentAccountAsync(cancellationToken) is not null;

    private async Task<AzureAppReference?> FindAppAsync(PublishProfile profile, CancellationToken cancellationToken)
    {
        if (AzureAppReference.TryParseResourceId(profile[ProfileProperties.ResourceId]) is { } fromResourceId)
        {
            return fromResourceId;
        }

        if (profile[ProfileProperties.PubwizAzureAppName] is { } name && profile[ProfileProperties.PubwizAzureResourceGroup] is { } group)
        {
            return new AzureAppReference(profile[ProfileProperties.PubwizAzureSubscriptionId], group, name);
        }

        // Imported .publishsettings: find the app with the same Kudu site in the active subscription.
        var scmHost = AzureProfile.GetScmUrl(profile).Host;
        var apps = await az.ListWebAppsAsync(cancellationToken);
        var match = apps.FirstOrDefault(a => string.Equals(a.ScmUrl.Host, scmHost, StringComparison.OrdinalIgnoreCase));
        return match is null ? null : new AzureAppReference(SubscriptionId: null, match.ResourceGroup, match.Name);
    }
}
