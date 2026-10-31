namespace Pubwiz.Core.Profiles;

/// <summary>
/// Finds and saves the publish profiles of a project, in <c>Properties/PublishProfiles/</c>
/// (the folder Visual Studio and the Web SDK use).
/// </summary>
public static class PublishProfileStore
{
    public static string GetProfilesFolder(string projectDirectory) => Path.Combine(projectDirectory, "Properties", "PublishProfiles");

    /// <summary>
    /// Lists the project's profiles, sorted by name. Files that can't be parsed are skipped.
    /// </summary>
    public static IReadOnlyList<PublishProfile> List(string projectDirectory)
    {
        var folder = GetProfilesFolder(projectDirectory);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.EnumerateFiles(folder, $"*{PubxmlSerializer.FileExtension}")
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(TryRead)
            .OfType<PublishProfile>()
            .ToList();
    }

    /// <summary>
    /// Saves a profile into the project's profiles folder and returns it with its file path set.
    /// </summary>
    /// <param name="overwrite">Replace an existing profile with the same name.</param>
    /// <exception cref="IOException">A profile with that name exists and <paramref name="overwrite"/> is <c>false</c>.</exception>
    public static PublishProfile Save(string projectDirectory, PublishProfile profile, bool overwrite = false)
    {
        var filePath = GetProfilePath(projectDirectory, profile.Name);
        if (File.Exists(filePath) && !overwrite)
        {
            throw new IOException($"A publish profile named '{profile.Name}' already exists.");
        }

        var saved = profile.CopyAs(profile.Name, filePath);
        PubxmlSerializer.Write(saved, filePath);
        return saved;
    }

    public static bool Exists(string projectDirectory, string profileName) => File.Exists(GetProfilePath(projectDirectory, profileName));

    /// <summary>
    /// Turns any text into a valid profile (file) name, e.g. <c>my-site - Web Deploy</c> stays as is
    /// but <c>a/b:c</c> becomes <c>a_b_c</c>.
    /// </summary>
    public static string ToValidName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        return cleaned.Length > 0 ? cleaned : "Profile";
    }

    private static string GetProfilePath(string projectDirectory, string profileName) =>
        Path.Combine(GetProfilesFolder(projectDirectory), ToValidName(profileName) + PubxmlSerializer.FileExtension);

    private static PublishProfile? TryRead(string filePath)
    {
        try
        {
            return PubxmlSerializer.Read(filePath);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}
