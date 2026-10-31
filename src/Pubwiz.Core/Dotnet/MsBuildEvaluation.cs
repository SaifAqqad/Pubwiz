namespace Pubwiz.Core.Dotnet;

/// <summary>
/// Evaluated MSBuild properties and items of a project, as returned by <c>dotnet msbuild -getProperty/-getItem</c>.
/// </summary>
public sealed class MsBuildEvaluation(IReadOnlyDictionary<string, string> properties, IReadOnlyDictionary<string, IReadOnlyList<MsBuildItem>> items)
{
    public IReadOnlyDictionary<string, string> Properties { get; } = properties;

    public IReadOnlyDictionary<string, IReadOnlyList<MsBuildItem>> Items { get; } = items;

    /// <summary>
    /// The property value, or <c>null</c> when it's empty or wasn't requested.
    /// </summary>
    public string? GetProperty(string name) => Properties.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>
    /// <c>true</c> when the property is set to "true" (case-insensitive), like MSBuild conditions.
    /// </summary>
    public bool IsTrue(string name) => string.Equals(GetProperty(name), "true", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<MsBuildItem> GetItems(string type) => Items.TryGetValue(type, out var list) ? list : [];
}

/// <summary>
/// One MSBuild item, e.g. a <c>PackageReference</c>.
/// </summary>
/// <param name="Identity">The item's <c>Include</c> value.</param>
/// <param name="Metadata">Item metadata such as <c>Version</c>.</param>
public sealed record MsBuildItem(string Identity, IReadOnlyDictionary<string, string> Metadata);
