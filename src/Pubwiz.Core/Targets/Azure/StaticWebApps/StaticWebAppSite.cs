using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pubwiz.Core.Targets.Azure.StaticWebApps;

/// <summary>
/// The site that goes to Static Web Apps, and the settings StaticSitesClient needs to upload it.
/// </summary>
public static class StaticWebAppSite
{
    public const string ConfigFileName = "staticwebapp.config.json";

    /// <summary>
    /// The folder with the site's files: <c>wwwroot</c> in a Blazor WebAssembly publish output (the rest is for hosting
    /// it in IIS), otherwise the output itself.
    /// </summary>
    public static string FindSiteFolder(string publishDirectory)
    {
        var wwwroot = Path.Combine(publishDirectory, "wwwroot");
        return File.Exists(Path.Combine(wwwroot, "index.html")) ? wwwroot : publishDirectory;
    }

    /// <summary>
    /// Adds a <c>staticwebapp.config.json</c> when the site has none. Without it, opening a page of the app directly
    /// (e.g. <c>/counter</c>) is a 404, because only <c>index.html</c> exists; this makes Static Web Apps serve
    /// <c>index.html</c> for such paths, as Microsoft's Blazor guide does, while missing framework files stay 404s.
    /// </summary>
    /// <returns><c>true</c> when it was added.</returns>
    /// <seealso href="https://learn.microsoft.com/azure/static-web-apps/deploy-blazor"/>
    public static bool EnsureConfig(string siteFolder)
    {
        var path = Path.Combine(siteFolder, ConfigFileName);
        if (File.Exists(path))
        {
            return false;
        }

        var config = new JsonObject
        {
            ["navigationFallback"] = new JsonObject
            {
                ["rewrite"] = "/index.html",
                ["exclude"] = new JsonArray("/_framework/*", "/_content/*", "/css/*")
            }
        };

        File.WriteAllText(path, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }

    /// <summary>
    /// The environment variables StaticSitesClient reads, set the way <c>swa deploy</c> sets them for an app that's
    /// already built: no build, the site folder as the app location, and its config file found there.
    /// </summary>
    /// <param name="siteFolder">The folder to upload.</param>
    /// <param name="deploymentToken">The app's deployment token.</param>
    /// <param name="environment">A preview environment, or <c>null</c> for production.</param>
    public static Dictionary<string, string?> CreateClientEnvironment(string siteFolder, string deploymentToken, string? environment)
    {
        var variables = new Dictionary<string, string?>
        {
            ["DEPLOYMENT_ACTION"] = "upload",
            ["DEPLOYMENT_PROVIDER"] = "SwaCli",
            ["REPOSITORY_BASE"] = siteFolder,
            ["SKIP_APP_BUILD"] = "true",
            ["SKIP_API_BUILD"] = "true",
            ["DEPLOYMENT_TOKEN"] = deploymentToken,

            // With SKIP_APP_BUILD, the client ignores OUTPUT_LOCATION and uploads APP_LOCATION.
            ["APP_LOCATION"] = siteFolder,
            ["CONFIG_FILE_LOCATION"] = siteFolder,
            ["VERBOSE"] = "false"
        };

        // Like swa deploy: production is the default, so it's only named for other environments.
        if (environment is not null && !environment.Equals("production", StringComparison.OrdinalIgnoreCase) && !environment.Equals("prod", StringComparison.OrdinalIgnoreCase))
        {
            variables["DEPLOYMENT_ENVIRONMENT"] = environment;
        }

        return variables;
    }
}
