using System.Text.Json.Serialization;

namespace Pubwiz.Core.Targets.Azure;

/// <summary>A subscription the signed-in user can use (<c>az account list</c>).</summary>
public sealed record AzureSubscription(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("isDefault")] bool IsDefault,
    [property: JsonPropertyName("user")] AzureUser? User);

public sealed record AzureUser([property: JsonPropertyName("name")] string Name);

/// <summary>An App Service app or Function App (<c>az webapp list</c> / <c>az functionapp list</c>).</summary>
/// <param name="Kind">e.g. <c>app</c>, <c>app,linux</c>, <c>app,linux,container</c>, <c>functionapp,linux</c>.</param>
public sealed record AzureWebApp(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("resourceGroup")] string ResourceGroup,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("defaultHostName")] string DefaultHostName,
    [property: JsonPropertyName("state")] string? State)
{
    public bool IsLinux => Kind.Contains("linux", StringComparison.OrdinalIgnoreCase);

    public bool IsContainer => Kind.Contains("container", StringComparison.OrdinalIgnoreCase);

    public bool IsFunctionApp => Kind.Contains("functionapp", StringComparison.OrdinalIgnoreCase);

    /// <summary>The Kudu site, e.g. <c>https://contoso.scm.azurewebsites.net/</c>.</summary>
    public Uri ScmUrl
    {
        get
        {
            // contoso.azurewebsites.net -> contoso.scm.azurewebsites.net (also for sovereign clouds)
            var dot = DefaultHostName.IndexOf('.');
            return new Uri($"https://{DefaultHostName[..dot]}.scm{DefaultHostName[dot..]}/");
        }
    }
}

/// <summary>Where an App Service app is, as needed by <c>az webapp</c> commands.</summary>
/// <param name="SubscriptionId">The subscription, or <c>null</c> for the active one.</param>
/// <param name="ResourceGroup">The app's resource group.</param>
/// <param name="Name">The app's name.</param>
/// <param name="Slot">The deployment slot, or <c>null</c> for the production slot.</param>
public sealed record AzureAppReference(string? SubscriptionId, string ResourceGroup, string Name, string? Slot = null)
{
    /// <summary>
    /// Parses <c>/subscriptions/{id}/resourceGroups/{group}/providers/Microsoft.Web/sites/{app}[/slots/{slot}]</c>.
    /// </summary>
    /// <returns>The app, or <c>null</c> when <paramref name="resourceId"/> isn't an App Service resource ID.</returns>
    public static AzureAppReference? TryParseResourceId(string? resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return null;
        }

        var parts = resourceId.Trim().Trim('/').Split('/');
        string? Segment(string name)
        {
            var index = Array.FindIndex(parts, p => p.Equals(name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < parts.Length ? parts[index + 1] : null;
        }

        var providerIndex = Array.FindIndex(parts, p => p.Equals("Microsoft.Web", StringComparison.OrdinalIgnoreCase));
        if (providerIndex < 0 || Segment("resourceGroups") is not { } group || Segment("sites") is not { } site)
        {
            return null;
        }

        return new AzureAppReference(Segment("subscriptions"), group, site, Segment("slots"));
    }
}

/// <summary>An Azure Container Registry (<c>az acr list</c>).</summary>
/// <param name="LoginServer">e.g. <c>contoso.azurecr.io</c>.</param>
public sealed record AzureContainerRegistry(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("resourceGroup")] string ResourceGroup,
    [property: JsonPropertyName("loginServer")] string LoginServer);

/// <summary>An Azure Container App (<c>az containerapp list</c>).</summary>
/// <param name="Fqdn">The ingress host name, if ingress is enabled.</param>
public sealed record AzureContainerApp(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("resourceGroup")] string ResourceGroup,
    [property: JsonPropertyName("fqdn")] string? Fqdn);
