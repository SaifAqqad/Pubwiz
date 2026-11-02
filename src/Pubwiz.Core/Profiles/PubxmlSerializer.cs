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

        return profile;
    }

    public static void Write(PublishProfile profile, string filePath)
    {
        var propertyGroup = new XElement("PropertyGroup", profile.Properties.Select(p => new XElement(p.Key, p.Value)));
        var document = new XDocument(
            new XComment(" Created by Pubwiz. Compatible with Visual Studio and `dotnet publish -p:PublishProfile=<name>` "),
            new XElement("Project", propertyGroup));

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
