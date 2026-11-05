namespace Pubwiz.Core.Targets.Azure;

/// <summary>
/// Builds Azure resource IDs, which Visual Studio stores in profiles as <c>ResourceId</c>, e.g.
/// <c>/subscriptions/{id}/resourceGroups/{group}/providers/Microsoft.Web/sites/{app}</c>.
/// </summary>
public static class AzureResourceIds
{
    public static string WebApp(string subscriptionId, string resourceGroup, string name) =>
        Create(subscriptionId, resourceGroup, "Microsoft.Web/sites", name);

    public static string ContainerApp(string subscriptionId, string resourceGroup, string name) =>
        Create(subscriptionId, resourceGroup, "Microsoft.App/containerApps", name);

    public static string ContainerRegistry(string subscriptionId, string resourceGroup, string name) =>
        Create(subscriptionId, resourceGroup, "Microsoft.ContainerRegistry/registries", name);

    /// <summary>The resource group of a resource ID, or <c>null</c> when it has none.</summary>
    public static string? GetResourceGroup(string? resourceId) => GetSegment(resourceId, "resourceGroups");

    /// <summary>The resource's own name: the last part of the ID.</summary>
    public static string? GetName(string? resourceId) =>
        string.IsNullOrWhiteSpace(resourceId) ? null : resourceId.Trim().TrimEnd('/').Split('/')[^1];

    private static string Create(string subscriptionId, string resourceGroup, string type, string name) =>
        $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/{type}/{name}";

    private static string? GetSegment(string? resourceId, string name)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return null;
        }

        var parts = resourceId.Trim().Trim('/').Split('/');
        var index = Array.FindIndex(parts, p => p.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < parts.Length ? parts[index + 1] : null;
    }
}
