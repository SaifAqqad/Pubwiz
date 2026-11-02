namespace Pubwiz.Core.Profiles;

/// <summary>
/// An MSBuild item in a profile, e.g. a <c>MsDeploySkipRules</c> entry:
/// <c>&lt;MsDeploySkipRules Include="KeepUploads"&gt;&lt;ObjectName&gt;dirPath&lt;/ObjectName&gt;…&lt;/MsDeploySkipRules&gt;</c>.
/// </summary>
/// <param name="Type">The item type (the element name), e.g. <c>MsDeploySkipRules</c>.</param>
/// <param name="Include">The <c>Include</c> attribute.</param>
/// <param name="Metadata">The child elements, by name (case-insensitive).</param>
public sealed record ProfileItem(string Type, string Include, IReadOnlyDictionary<string, string> Metadata)
{
    /// <summary>A metadata value, or <c>null</c> when it's missing or empty.</summary>
    public string? this[string name] => Metadata.TryGetValue(name, out var value) && value.Trim() is { Length: > 0 } trimmed ? trimmed : null;
}

/// <summary>
/// A publish profile: a named set of MSBuild properties (and a few items), stored as a Visual Studio compatible .pubxml file.
/// </summary>
/// <remarks>
/// Properties keep their insertion order so a saved file reads naturally.
/// Secrets (passwords) are never stored here; see the secret store.
/// </remarks>
public sealed class PublishProfile
{
    private readonly List<KeyValuePair<string, string>> _properties = [];

    public PublishProfile(string name, string? filePath = null)
    {
        Name = name;
        FilePath = filePath;
    }

    /// <summary>The profile name: the .pubxml file name without extension.</summary>
    public string Name { get; }

    /// <summary>Where the profile is stored, or <c>null</c> when it hasn't been saved yet.</summary>
    public string? FilePath { get; }

    public IReadOnlyList<KeyValuePair<string, string>> Properties => _properties;

    /// <summary>The profile's MSBuild items, e.g. Web Deploy skip rules, in file order.</summary>
    public List<ProfileItem> Items { get; } = [];

    /// <summary>
    /// How the profile publishes. Web projects use <c>WebPublishMethod</c>; Visual Studio writes <c>PublishProtocol</c>
    /// for other projects (the Web SDK falls back the same way).
    /// </summary>
    public PublishMethod Method => PublishMethodParser.Parse(this[ProfileProperties.WebPublishMethod] ?? this[ProfileProperties.PublishProtocol]);

    /// <summary>
    /// Gets or sets a property by name (case-insensitive). Getting a missing property returns <c>null</c>;
    /// setting <c>null</c> removes it.
    /// </summary>
    /// <remarks>
    /// Values are read without surrounding whitespace (e.g. a hand-edited value on its own line), and an empty value
    /// reads as <c>null</c>, as in MSBuild, where an empty property is an unset one: Visual Studio writes elements like
    /// <c>&lt;UserName /&gt;</c> for profiles that sign in another way.
    /// <see cref="Properties"/> still has them, so saving a profile keeps the file as it was.
    /// </remarks>
    public string? this[string name]
    {
        get => _properties.FirstOrDefault(p => IsSameName(p.Key, name)).Value?.Trim() is { Length: > 0 } value ? value : null;
        set
        {
            var index = _properties.FindIndex(p => IsSameName(p.Key, name));
            if (value is null)
            {
                if (index >= 0)
                {
                    _properties.RemoveAt(index);
                }

                return;
            }

            var property = new KeyValuePair<string, string>(index >= 0 ? _properties[index].Key : name, value);
            if (index >= 0)
            {
                _properties[index] = property;
            }
            else
            {
                _properties.Add(property);
            }
        }
    }

    /// <summary>
    /// Returns a copy of this profile under a new name and file path.
    /// </summary>
    public PublishProfile CopyAs(string name, string? filePath)
    {
        var copy = new PublishProfile(name, filePath);
        copy._properties.AddRange(_properties);
        copy.Items.AddRange(Items);
        return copy;
    }

    private static bool IsSameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
