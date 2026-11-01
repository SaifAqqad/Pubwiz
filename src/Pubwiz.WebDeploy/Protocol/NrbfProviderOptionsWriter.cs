using System.Text;

namespace Pubwiz.WebDeploy.Protocol;

/// <summary>
/// Writes a <c>Microsoft.Web.Deployment.DeploymentProviderOptions</c> object in the .NET
/// Binary Formatter wire format (MS-NRBF). Web Deploy uses this one serialized class in two
/// places: the <c>MSDeploy.ProviderOptions</c> request header, and the per-object "resolver
/// options" blobs embedded in the sync stream.
/// </summary>
/// <remarks>
/// MS-NRBF is a published Microsoft specification, so this is a hand-written encoder for the
/// exact record sequence Web Deploy emits — never <c>BinaryFormatter</c> itself, which is unsafe.
/// The output was checked byte-for-byte against captures and with <c>System.Formats.Nrbf</c>.
/// Only <c>providerName</c> and <c>path</c> are ever set; <c>names</c>/<c>values</c> are empty.
/// </remarks>
public static class NrbfProviderOptionsWriter
{
    // The assembly that owns the type, exactly as Web Deploy 4.0 (msdeploy 7.1.2001) serializes it,
    // including the lower-case "web.deployment" in the assembly display name.
    private const string LibraryName =
        "Microsoft.web.deployment, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35";
    private const string ClassName = "Microsoft.Web.Deployment.DeploymentProviderOptions";

    // MS-NRBF record type tags.
    private const byte SerializationHeader = 0x00;
    private const byte ClassWithMembersAndTypes = 0x05;
    private const byte BinaryObjectString = 0x06;
    private const byte MemberReference = 0x09;
    private const byte BinaryLibrary = 0x0C;
    private const byte ArraySingleString = 0x11;
    private const byte MessageEnd = 0x0B;

    // BinaryTypeEnumeration values.
    private const byte TypeString = 1;
    private const byte TypeStringArray = 6;

    /// <returns>The raw MS-NRBF bytes (uncompressed).</returns>
    public static byte[] Write(string providerName, string path)
    {
        using var stream = new MemoryStream();
        var writer = new BinaryWriter(stream, new UTF8Encoding(false));

        // Record ids: the root class is 1, the library is 2, then the member values get 3..6.
        const int classId = 1, libraryId = 2, providerNameId = 3, pathId = 4, namesId = 5, valuesId = 6;

        // SerializationHeaderRecord: root object id, header id (-1), format version 1.0.
        writer.Write(SerializationHeader);
        writer.Write(classId);
        writer.Write(-1);
        writer.Write(1);
        writer.Write(0);

        // BinaryLibrary: the assembly the class lives in.
        writer.Write(BinaryLibrary);
        writer.Write(libraryId);
        writer.Write(LibraryName); // BinaryWriter writes a 7-bit length prefix, which is what NRBF uses.

        // ClassWithMembersAndTypes: name, members, their types, and the owning library.
        writer.Write(ClassWithMembersAndTypes);
        writer.Write(classId);
        writer.Write(ClassName);
        writer.Write(4); // member count
        writer.Write("providerName");
        writer.Write("path");
        writer.Write("names");
        writer.Write("values");
        writer.Write(TypeString);       // providerName
        writer.Write(TypeString);       // path
        writer.Write(TypeStringArray);  // names
        writer.Write(TypeStringArray);  // values
        writer.Write(libraryId);        // the class's library reference

        // Member values, in declaration order.
        WriteStringRecord(writer, providerNameId, providerName);
        WriteStringRecord(writer, pathId, path);
        WriteMemberReference(writer, namesId);
        WriteMemberReference(writer, valuesId);

        // The two empty string arrays the references point at.
        WriteEmptyStringArray(writer, namesId);
        WriteEmptyStringArray(writer, valuesId);

        writer.Write(MessageEnd);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteStringRecord(BinaryWriter writer, int objectId, string value)
    {
        writer.Write(BinaryObjectString);
        writer.Write(objectId);
        writer.Write(value);
    }

    private static void WriteMemberReference(BinaryWriter writer, int objectId)
    {
        writer.Write(MemberReference);
        writer.Write(objectId);
    }

    private static void WriteEmptyStringArray(BinaryWriter writer, int objectId)
    {
        writer.Write(ArraySingleString);
        writer.Write(objectId);
        writer.Write(0); // length
    }
}
