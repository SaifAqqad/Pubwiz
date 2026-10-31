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
}
