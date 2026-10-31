namespace Pubwiz.Core.Profiles;

/// <summary>
/// A publish profile: a named set of MSBuild properties, stored as a Visual Studio compatible .pubxml file.
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

    public PublishMethod Method => PublishMethodParser.Parse(this[ProfileProperties.WebPublishMethod]);

    /// <summary>
    /// Gets or sets a property by name (case-insensitive). Getting a missing property returns <c>null</c>;
    /// setting <c>null</c> removes it.
    /// </summary>
    public string? this[string name]
    {
        get => _properties.FirstOrDefault(p => IsSameName(p.Key, name)).Value;
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
        return copy;
    }

    private static bool IsSameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
