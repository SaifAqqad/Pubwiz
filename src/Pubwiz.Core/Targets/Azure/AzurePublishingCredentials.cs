using Pubwiz.Core.Profiles;

namespace Pubwiz.Core.Targets.Azure;

/// <summary>What <see cref="AzurePublishingCredentials.TryGetAsync"/> found.</summary>
public abstract record AzureCredentialsResult
{
    /// <param name="Password">The publishing password.</param>
    /// <param name="AppName">The app it came from, e.g. <c>contoso</c> or <c>contoso/staging</c>.</param>
    public sealed record Found(string Password, string AppName) : AzureCredentialsResult;

    /// <param name="Reason">Why not, e.g. "az isn't signed in" (plain text, shown before asking).</param>
    public sealed record NotFound(string Reason) : AzureCredentialsResult;
}

/// <summary>
/// Gets the publishing password of an existing Azure App Service profile (Zip Deploy, or Web Deploy to an App Service)
/// from Azure, with the signed-in <c>az</c>, so it doesn't have to be typed or stored.
/// </summary>
/// <remarks>
/// The app is found from the profile's resource ID (Visual Studio profiles), the Pubwiz Azure properties, or else by
/// matching its SCM host against the apps in the active subscription, then the others (e.g. imported .publishsettings).
/// Only a password for the profile's own user name is used, so user-level deployment credentials are still asked for.
/// </remarks>
public sealed class AzurePublishingCredentials(AzCli az)
{
    private Task<IReadOnlyList<AzureSubscription>?>? _subscriptions;

    /// <summary><c>true</c> for Azure App Service profiles that sign in with a publishing user name and password.</summary>
    public static bool Applies(PublishProfile profile) => AzureProfile.IsAppService(profile) && !AzureProfile.UsesAzCli(profile);

    /// <returns>The password, or why it couldn't be fetched. Never throws for az errors: the caller falls back to asking.</returns>
    public async Task<AzureCredentialsResult> TryGetAsync(PublishProfile profile, CancellationToken cancellationToken)
    {
        if (!Applies(profile))
        {
            return new AzureCredentialsResult.NotFound("it isn't an Azure App Service profile with a user name");
        }

        if (!az.IsInstalled)
        {
            return new AzureCredentialsResult.NotFound("the Azure CLI (az) isn't installed");
        }

        try
        {
            if (await (_subscriptions ??= ListSubscriptionsAsync(cancellationToken)) is not { } subscriptions)
            {
                return new AzureCredentialsResult.NotFound("az isn't signed in (run 'az login')");
            }

            if (await FindAppAsync(profile, subscriptions, cancellationToken) is not { } app)
            {
                return new AzureCredentialsResult.NotFound($"no app with the Kudu site {AzureProfile.GetScmUrl(profile).Host} was found in your subscriptions");
            }

            var appName = app.Slot is null ? app.Name : $"{app.Name}/{app.Slot}";
            var profiles = await az.ListPublishingProfilesAsync(app, cancellationToken);
            return PickPassword(profiles, profile[ProfileProperties.UserName]) is { } password
                ? new AzureCredentialsResult.Found(password, appName)
                : new AzureCredentialsResult.NotFound($"{appName} has no publishing credentials for {profile[ProfileProperties.UserName]}");
        }
        catch (AzCliException ex)
        {
            return new AzureCredentialsResult.NotFound(FirstLine(ex.Message));
        }
        catch (ArgumentException ex)
        {
            return new AzureCredentialsResult.NotFound(ex.Message);
        }
    }

    /// <summary>The password of the Web Deploy or Zip Deploy entry for <paramref name="userName"/> (they share credentials).</summary>
    internal static string? PickPassword(IReadOnlyList<AzurePublishingProfile> profiles, string? userName) => profiles
        .Where(p => p.PublishMethod is { } method
                    && (method.Equals("MSDeploy", StringComparison.OrdinalIgnoreCase) || method.Equals("ZipDeploy", StringComparison.OrdinalIgnoreCase)))
        .FirstOrDefault(p => p.Password is { Length: > 0 } && string.Equals(p.UserName, userName, StringComparison.OrdinalIgnoreCase))
        ?.Password;

    /// <returns>The subscriptions, or <c>null</c> when az isn't signed in.</returns>
    private async Task<IReadOnlyList<AzureSubscription>?> ListSubscriptionsAsync(CancellationToken cancellationToken)
    {
        if (await az.GetCurrentAccountAsync(cancellationToken) is null)
        {
            return null;
        }

        // The active one first, so the common case needs one app list.
        return (await az.ListSubscriptionsAsync(cancellationToken)).OrderByDescending(s => s.IsDefault).ToList();
    }

    private async Task<AzureAppReference?> FindAppAsync(PublishProfile profile, IReadOnlyList<AzureSubscription> subscriptions, CancellationToken cancellationToken)
    {
        if (AzureAppReference.TryParseResourceId(profile[ProfileProperties.ResourceId]) is { } fromResourceId)
        {
            return fromResourceId;
        }

        if (profile[ProfileProperties.PubwizAzureAppName] is { } name && profile[ProfileProperties.PubwizAzureResourceGroup] is { } group)
        {
            return new AzureAppReference(profile[ProfileProperties.PubwizAzureSubscriptionId], group, name);
        }

        var scmHost = AzureProfile.GetScmUrl(profile).Host;

        // Imported .publishsettings: find the app with the same Kudu site.
        foreach (var subscription in subscriptions)
        {
            var apps = await az.ListWebAppsAsync(subscription.Id, cancellationToken);
            if (apps.FirstOrDefault(a => string.Equals(a.ScmUrl.Host, scmHost, StringComparison.OrdinalIgnoreCase)) is { } match)
            {
                return new AzureAppReference(subscription.Id, match.ResourceGroup, match.Name);
            }
        }

        return null;
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? text;
}
