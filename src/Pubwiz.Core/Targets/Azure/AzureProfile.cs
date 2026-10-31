using Pubwiz.Core.Profiles;
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
