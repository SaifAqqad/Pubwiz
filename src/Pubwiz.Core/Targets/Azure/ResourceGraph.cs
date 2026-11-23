using System.Text.Json.Serialization;

namespace Pubwiz.Core.Targets.Azure;

/// <summary>
/// An Azure Resource Graph query: <c>POST https://management.azure.com/providers/Microsoft.ResourceGraph/resources</c>.
/// </summary>
/// <param name="Subscriptions">The subscriptions to look in.</param>
/// <param name="Query">The query, in KQL.</param>
/// <param name="Options">Paging.</param>
internal sealed record ResourceGraphRequest(
    [property: JsonPropertyName("subscriptions")] IReadOnlyList<string> Subscriptions,
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("options")] ResourceGraphOptions Options)
{
    public const string Url = "https://management.azure.com/providers/Microsoft.ResourceGraph/resources?api-version=2022-10-01";

    /// <summary>The most rows Resource Graph returns at once.</summary>
    public const int PageSize = 1000;
}

/// <param name="Top">Rows per page.</param>
/// <param name="SkipToken">Where the next page starts: the previous page's <see cref="ResourceGraphResponse{T}.SkipToken"/>.</param>
internal sealed record ResourceGraphOptions(
    [property: JsonPropertyName("$top")] int Top,
    [property: JsonPropertyName("$skipToken"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SkipToken);

/// <param name="Data">The rows of this page.</param>
/// <param name="SkipToken">Set when there are more rows: send it back to get the next page.</param>
internal sealed record ResourceGraphResponse<T>(
    [property: JsonPropertyName("data")] List<T> Data,
    [property: JsonPropertyName("$skipToken")] string? SkipToken);

/// <summary>
/// The Resource Graph query of <see cref="AzCli.ListWebAppsWithSlotsAsync"/>: every App Service app, Function App and
/// deployment slot, which are then put together into apps with their slots.
/// </summary>
internal static class WebAppsQuery
{
    public const string Query = """
        resources
        | where type =~ 'microsoft.web/sites' or type =~ 'microsoft.web/sites/slots'
        | project id, name, kind, defaultHostName = tostring(properties.defaultHostName), state = tostring(properties.state)
        """;

    /// <summary>An app or a slot.</summary>
    /// <param name="Id">e.g. <c>.../sites/contoso</c> or <c>.../sites/contoso/slots/staging</c>.</param>
    /// <param name="Name">e.g. <c>contoso</c>, or <c>contoso/staging</c> for a slot.</param>
    internal sealed record Row(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("defaultHostName")] string? DefaultHostName,
        [property: JsonPropertyName("state")] string? State);

    /// <summary>The apps, each with its slots (found by the app in the slot's ID), sorted by name.</summary>
    /// <remarks>Rows without a kind or host name are skipped, as <c>az webapp list</c> does.</remarks>
    public static IReadOnlyList<AzureWebApp> ToWebApps(IEnumerable<Row> rows)
    {
        var parsed = rows
            .Where(row => row.DefaultHostName is { Length: > 0 })
            .Select(row => (Row: row, Reference: AzureAppReference.TryParseResourceId(row.Id)))
            .Where(item => item.Reference is not null)
            .ToList();

        var slotsByApp = parsed
            .Where(item => item.Reference!.Slot is not null)
            .ToLookup(item => Key(item.Reference!), item => new AzureDeploymentSlot(item.Reference!.Slot!, item.Row.DefaultHostName!, NullIfEmpty(item.Row.State)));

        return parsed
            .Where(item => item.Reference!.Slot is null && item.Row.Kind is { Length: > 0 })
            .Select(item => new AzureWebApp(item.Reference!.Name, item.Reference.ResourceGroup, item.Row.Kind!, item.Row.DefaultHostName!, NullIfEmpty(item.Row.State))
            {
                Slots = slotsByApp[Key(item.Reference)].OrderBy(slot => slot.Name, StringComparer.OrdinalIgnoreCase).ToList()
            })
            .OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Azure names aren't case-sensitive.</summary>
    private static string Key(AzureAppReference app) => $"{app.ResourceGroup}/{app.Name}".ToLowerInvariant();

    private static string? NullIfEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;
}
