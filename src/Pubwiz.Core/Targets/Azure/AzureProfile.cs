using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Targets.WebDeploy;

namespace Pubwiz.Core.Targets.Azure;

/// <summary>
/// Reads the Azure App Service settings of a profile, whichever way it was created
/// (Pubwiz, Visual Studio, or an imported .publishsettings file).
/// </summary>
public static class AzureProfile
{
    public const string AzCliAuthentication = "AzCli";

    /// <summary>
    /// Zip/One deploy profiles, and Web Deploy profiles pointing at an App Service SCM site
    /// (those can be deployed through Kudu instead of msdeploy, from any OS).
    /// </summary>
    public static bool IsAppService(PublishProfile profile) => profile.Method switch
    {
        PublishMethod.ZipDeploy or PublishMethod.OneDeploy => true,
        PublishMethod.MSDeploy => TryGetWebDeployScmUrl(profile) is not null,
        _ => false
    };

    public static bool IsWebJob(PublishProfile profile) => IsAppService(profile) && profile[ProfileProperties.WebJobName] is not null;

    public static bool IsTriggeredWebJob(PublishProfile profile) =>
        string.Equals(profile[ProfileProperties.WebJobType], "Triggered", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Linux or Windows, from <c>IsLinux</c> (Visual Studio, Pubwiz) or <c>PubwizAzureLinux</c> (earlier Pubwiz
    /// versions); <see cref="TargetPlatform.Any"/> when neither is set (e.g. imported .publishsettings).
    /// </summary>
    /// <remarks>
    /// Linux apps always run on x64. Windows apps run 32-bit unless the app's "64 Bit" platform setting is on, and
    /// WebJobs run like their app, so Windows apps are built for any CPU (see <see cref="TargetPlatform.AnyArchitecture"/>).
    /// </remarks>
    public static TargetPlatform GetPlatform(PublishProfile profile) =>
        (profile[ProfileProperties.IsLinux] ?? profile[ProfileProperties.PubwizAzureLinux])?.Trim().ToLowerInvariant() switch
        {
            "true" => TargetPlatform.Of(PublishOs.Linux),
            "false" => TargetPlatform.OfAnyArchitecture(PublishOs.Windows),
            _ => TargetPlatform.Any
        };

    /// <summary>
    /// The deployment slot the profile publishes to, e.g. <c>staging</c>, or <c>null</c> for the production slot.
    /// Read from the <c>ResourceId</c> (<c>.../sites/{app}/slots/{slot}</c>), or else from the site name of a slot's
    /// imported .publishsettings (see <see cref="GetSlotSiteName"/>).
    /// </summary>
    public static string? GetDeploymentSlot(PublishProfile profile)
    {
        if (AzureAppReference.TryParseResourceId(profile[ProfileProperties.ResourceId]) is { } app)
        {
            return app.Slot;
        }

        return IsAppService(profile) ? GetSlotSiteName(profile)?.Slot : null;
    }

    /// <summary>
    /// The app and slot of a deployment slot's .publishsettings, which name its site <c>contoso__staging</c> (the app,
    /// two underscores, the slot) in <c>msdeploySite</c> and in the user name (<c>$contoso__staging</c>).
    /// App names can't contain underscores, so the two can't be mixed up.
    /// </summary>
    /// <returns>The app and slot, or <c>null</c> when the profile isn't for a slot.</returns>
    public static (string App, string Slot)? GetSlotSiteName(PublishProfile profile)
    {
        var site = profile[ProfileProperties.DeployIisAppPath] ?? profile[ProfileProperties.UserName]?.TrimStart('$');
        var separator = site?.IndexOf("__", StringComparison.Ordinal) ?? -1;
        return separator > 0 && separator + 2 < site!.Length ? (site[..separator], site[(separator + 2)..]) : null;
    }

    /// <summary>Sign in with an az token when asked to, or when there are no publishing credentials.</summary>
    public static bool UsesAzCli(PublishProfile profile) =>
        string.Equals(profile[ProfileProperties.PubwizAzureAuthentication], AzCliAuthentication, StringComparison.OrdinalIgnoreCase)
        || profile[ProfileProperties.UserName] is null;

    /// <summary>The Kudu site, e.g. <c>https://contoso.scm.azurewebsites.net/</c>.</summary>
    /// <exception cref="ArgumentException">The profile has no usable URL.</exception>
    public static Uri GetScmUrl(PublishProfile profile)
    {
        if (profile.Method == PublishMethod.MSDeploy)
        {
            return TryGetWebDeployScmUrl(profile) ?? throw new ArgumentException($"'{profile.Name}' isn't an Azure App Service profile.", nameof(profile));
        }

        var publishUrl = profile[ProfileProperties.PublishUrl];
        if (publishUrl is null || !Uri.TryCreate(publishUrl.Contains("://") ? publishUrl : $"https://{publishUrl}", UriKind.Absolute, out var url))
        {
            throw new ArgumentException($"The profile '{profile.Name}' has no valid {ProfileProperties.PublishUrl}.", nameof(profile));
        }

        return new UriBuilder(Uri.UriSchemeHttps, url.Host).Uri;
    }

    /// <summary>The public site URL, falling back to the SCM host without ".scm".</summary>
    public static string GetSiteUrl(PublishProfile profile)
    {
        if (profile[ProfileProperties.SiteUrlToLaunchAfterPublish] is { } siteUrl)
        {
            return siteUrl;
        }

        var scmHost = GetScmUrl(profile).Host;
        return $"https://{scmHost.Replace(".scm.", ".", StringComparison.OrdinalIgnoreCase)}";
    }

    private static Uri? TryGetWebDeployScmUrl(PublishProfile profile)
    {
        try
        {
            var endpoint = WebDeployProfile.GetEndpoint(profile);
            return endpoint.IsAzure ? new UriBuilder(Uri.UriSchemeHttps, endpoint.ServiceUrl.Host).Uri : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
