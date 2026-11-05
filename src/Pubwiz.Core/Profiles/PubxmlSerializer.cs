using System.Xml.Linq;

namespace Pubwiz.Core.Profiles;

/// <summary>
/// Reads and writes publish profiles in the Visual Studio .pubxml format.
/// </summary>
/// <remarks>
/// A .pubxml file is a plain MSBuild project holding one <c>PropertyGroup</c>.
/// Older Visual Studio versions add the MSBuild 2003 XML namespace, so element names are matched by local name.
/// </remarks>
public static class PubxmlSerializer
{
    public const string FileExtension = ".pubxml";

    public static PublishProfile Read(string filePath)
    {
        var document = XDocument.Load(filePath);
        var profile = new PublishProfile(Path.GetFileNameWithoutExtension(filePath), filePath);

        var properties = document.Root?
            .Elements().Where(e => e.Name.LocalName == "PropertyGroup")
            .SelectMany(group => group.Elements()) ?? [];

        foreach (var property in properties)
        {
            profile[property.Name.LocalName] = property.Value.Trim();
        }

        var items = document.Root?
            .Elements().Where(e => e.Name.LocalName == "ItemGroup")
            .SelectMany(group => group.Elements()) ?? [];

        foreach (var item in items)
        {
            var metadata = item.Elements().ToDictionary(e => e.Name.LocalName, e => e.Value.Trim(), StringComparer.OrdinalIgnoreCase);
            profile.Items.Add(new ProfileItem(item.Name.LocalName, (string?)item.Attribute("Include") ?? string.Empty, metadata));
        }

        return profile;
    }

    public static void Write(PublishProfile profile, string filePath)
    {
        var propertyGroup = new XElement("PropertyGroup", profile.Properties.Select(p => new XElement(p.Key, p.Value)));
        var project = new XElement("Project", propertyGroup);
        if (profile.Items.Count > 0)
        {
            project.Add(new XElement("ItemGroup", profile.Items.Select(item => new XElement(item.Type,
                new XAttribute("Include", item.Include),
                item.Metadata.Select(m => new XElement(m.Key, m.Value))))));
        }

        // The same header Visual Studio writes.
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", standalone: null),
            new XComment(" https://go.microsoft.com/fwlink/?LinkID=208121. "),
            project);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        document.Save(filePath);
    }

    /// <summary>
    /// Sets one property in an existing .pubxml file, leaving everything else (other properties, item groups,
    /// comments, formatting) as it is. A missing property is added to the first <c>PropertyGroup</c>.
    /// </summary>
    /// <exception cref="InvalidDataException">The file has no <c>PropertyGroup</c>.</exception>
    public static void SetProperty(string filePath, string name, string value)
    {
        var document = XDocument.Load(filePath, LoadOptions.PreserveWhitespace);
        var groups = document.Root?.Elements().Where(e => e.Name.LocalName == "PropertyGroup").ToList() ?? [];
        if (groups.Count == 0)
        {
            throw new InvalidDataException($"{Path.GetFileName(filePath)} has no PropertyGroup.");
        }

        var existing = groups.SelectMany(group => group.Elements()).LastOrDefault(e => e.Name.LocalName == name);
        if (existing is not null)
        {
            existing.Value = value;
        }
        else
        {
            // Same namespace as the group, so files with the old MSBuild namespace stay valid.
            groups[0].Add(new XElement(groups[0].Name.Namespace + name, value));
        }

        document.Save(filePath, SaveOptions.DisableFormatting);
    }
}
