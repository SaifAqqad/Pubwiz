namespace Pubwiz.WebDeploy;

/// <summary>
/// A Web Deploy destination: the WMSVC handler URL and the IIS site/application.
/// </summary>
/// <param name="ServiceUrl">Full handler URL, e.g. <c>https://server:8172/msdeploy.axd?site=Default%20Web%20Site</c>.</param>
/// <param name="SiteName">The IIS site, e.g. <c>Default Web Site</c>.</param>
/// <param name="ApplicationPath">The application inside the site (e.g. <c>api</c>), or empty for the site root.</param>
public sealed record WebDeployEndpoint(Uri ServiceUrl, string SiteName, string ApplicationPath)
{
    /// <summary>The port WMSVC listens on by default.</summary>
    public const int DefaultPort = 8172;

    /// <summary>The IIS path deployed to, e.g. <c>Default Web Site/api</c> (what msdeploy calls the contentPath).</summary>
    public string IisPath => ApplicationPath.Length == 0 ? SiteName : $"{SiteName}/{ApplicationPath}";

    /// <summary><c>true</c> for Azure App Service, whose SCM site also accepts Kudu zip deploys.</summary>
    public bool IsAzure => ServiceUrl.Host.Contains(".scm.azurewebsites.", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds an endpoint from what users type (and what .pubxml files contain): a bare server name, <c>server:port</c>,
    /// or a full URL. Missing parts get the same defaults as the .NET SDK: https, port 8172, <c>/msdeploy.axd</c>, <c>?site=</c>.
    /// </summary>
    /// <param name="serviceUrl">e.g. <c>iis.contoso.com</c>, <c>iis:8172</c> or <c>https://iis:8172/msdeploy.axd</c>.</param>
    /// <param name="iisPath">e.g. <c>Default Web Site</c> or <c>Default Web Site/api</c>.</param>
    public static WebDeployEndpoint Create(string serviceUrl, string iisPath)
    {
        var segments = iisPath.Trim().Trim('/', '\\').Split(['/', '\\'], 2);
        var siteName = segments[0];
        var applicationPath = segments.Length > 1 ? segments[1].Trim('/', '\\').Replace('\\', '/') : string.Empty;

        return new WebDeployEndpoint(BuildServiceUrl(serviceUrl.Trim(), siteName), siteName, applicationPath);
    }

    private static Uri BuildServiceUrl(string serviceUrl, string siteName)
    {
        var hasScheme = serviceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || serviceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        var builder = new UriBuilder(hasScheme ? serviceUrl : $"https://{serviceUrl}");

        // UriBuilder fills in the scheme's default port (443), so check whether a port was actually written:
        // "server:9000" (no scheme) or "https://server:9000/..." (port appears in the text).
        var hasExplicitPort = hasScheme ? serviceUrl.Contains($":{builder.Port}", StringComparison.Ordinal) : serviceUrl.Contains(':');
        if (!hasExplicitPort)
        {
            builder.Port = DefaultPort;
        }

        if (builder.Path is "" or "/")
        {
            builder.Path = "msdeploy.axd";
        }

        if (string.IsNullOrEmpty(builder.Query))
        {
            builder.Query = $"site={Uri.EscapeDataString(siteName)}";
        }

        return builder.Uri;
    }
}
