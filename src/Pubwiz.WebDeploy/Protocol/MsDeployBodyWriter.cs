using System.IO.Compression;
using System.Text;

namespace Pubwiz.WebDeploy.Protocol;

/// <summary>
/// Serializes the body of a Web Deploy <c>Sync</c> request: msdeploy's own little-endian binary
/// object stream describing the source <c>iisApp</c> tree. The format was reverse-engineered from
/// captures and is documented in <c>docs/protocol.md</c>.
/// </summary>
/// <remarks>
/// A sync runs in two passes sharing the same tree. Pass 1 sends metadata only; the server replies
/// (via the trace long-poll) with the object ids whose content it still needs, and pass 2 resends
/// the tree with the content of exactly those files.
/// </remarks>
internal sealed class MsDeployBodyWriter
{
    // Stream section tags (u16) that precede the object tree.
    private const ushort SectionSystemInfo = 4;
    private const ushort SectionParameters = 10;

    // Object-stream opcodes (u16).
    private const ushort OpBeginContent = 5; // a file's content stream follows
    private const ushort OpContentChunk = 6; // one content chunk (length-prefixed); length 0 ends the stream
    private const ushort OpClose = 3;         // close the object with the given id
    private const ushort OpFile = 9;          // open a file object
    private const ushort OpDirectory = 8;     // open a directory object

    // Fixed per-node values the official client sends for a plain folder sync.
    private const int FileAttributesArchive = 0x20;
    private const int DirectoryAttributes = 0x10;
    private const string SecurityDescriptor = "D:"; // an empty DACL; ACLs are not synced

    // The id of the dirPath object (the sync root) in the fixed scaffold; file/dir ids continue from here.
    private const int DirPathObjectId = 4;
    private const int FirstChildObjectId = 5;

    private readonly string sourcePath;
    private readonly string machineName;

    public MsDeployBodyWriter(string sourcePath, string machineName)
    {
        this.sourcePath = sourcePath;
        this.machineName = machineName;
    }

    /// <summary>First pass: the whole tree as metadata, with no file content.</summary>
    public byte[] WriteMetadataPass(SourceDirectory root)
        => Write(root, contentObjectIds: null);

    /// <summary>Second pass: the tree again, with content for the files whose ids the server asked for.</summary>
    public byte[] WriteContentPass(SourceDirectory root, IReadOnlySet<int> contentObjectIds)
        => Write(root, contentObjectIds);

    private byte[] Write(SourceDirectory root, IReadOnlySet<int>? contentObjectIds)
    {
        AssignObjectIds(root);

        using var stream = new MemoryStream();
        var w = new BinaryWriter(stream, new UTF8Encoding(false));

        WriteSection(w, SectionSystemInfo, string.Format(MsDeployProtocol.SystemInfoTemplate, machineName));
        WriteSection(w, SectionParameters, MsDeployProtocol.Parameters);
        WriteScaffold(w);
        WriteChildren(w, root, contentObjectIds);

        w.Flush();
        return stream.ToArray();
    }

    /// <summary>Numbers the tree depth-first (dir before its children), starting after the scaffold's ids.</summary>
    private static void AssignObjectIds(SourceDirectory root)
    {
        var next = FirstChildObjectId;
        void Visit(IReadOnlyList<SourceNode> nodes)
        {
            foreach (var node in nodes)
            {
                node.ObjectId = next++;
                if (node is SourceDirectory dir)
                {
                    Visit(dir.Children);
                }
            }
        }

        Visit(root.Children);
    }

    private static void WriteSection(BinaryWriter w, ushort tag, string xml)
    {
        var bytes = Encoding.UTF8.GetBytes(xml);
        w.Write(tag);
        w.Write(bytes.Length);
        w.Write(bytes);
    }

    /// <summary>
    /// Writes the fixed provider scaffold, splicing in the regenerated resolver-options blobs and the
    /// source path between the captured constant segments (see <see cref="MsDeployProtocol"/>).
    /// </summary>
    private void WriteScaffold(BinaryWriter w)
    {
        var s = MsDeployProtocol.ScaffoldSegments;
        w.Write(s[0]); WriteResolverBlob(w, "iisApp");
        w.Write(s[1]); WriteResolverBlob(w, "iisApp");
        w.Write(s[2]); WriteBodyString(w, sourcePath);
        w.Write(s[3]); WriteBodyString(w, sourcePath);
        w.Write(s[4]); WriteResolverBlob(w, "contentPath");
        w.Write(s[5]); WriteBodyString(w, sourcePath);
        w.Write(s[6]); WriteResolverBlob(w, "dirPath");
        w.Write(s[7]); WriteBodyString(w, sourcePath);
        w.Write(s[8]);
    }

    private void WriteChildren(BinaryWriter w, SourceDirectory root, IReadOnlySet<int>? contentObjectIds)
    {
        // Enter the dirPath children list (constant token), emit the tree, then close dirPath.
        w.Write(OpClose);
        w.Write(3);
        WriteNodes(w, root.Children, DirPathObjectId, contentObjectIds);
        w.Write(OpClose);
        w.Write(DirPathObjectId);
    }

    private void WriteNodes(BinaryWriter w, IReadOnlyList<SourceNode> nodes, int parentId, IReadOnlySet<int>? contentObjectIds)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case SourceFile file:
                    WriteFile(w, file, parentId, contentObjectIds);
                    break;
                case SourceDirectory dir:
                    WriteDirectory(w, dir, parentId, contentObjectIds);
                    break;
            }
        }
    }

    private void WriteFile(BinaryWriter w, SourceFile file, int parentId, IReadOnlySet<int>? contentObjectIds)
    {
        w.Write(OpFile);
        w.Write(file.ObjectId);
        w.Write(parentId);
        WriteBodyString(w, file.Name);
        w.Write(8); w.Write(file.Length);                 // size (Int64)
        w.Write(4); w.Write(FileAttributesArchive);       // attributes (Int32)
        w.Write(8); w.Write(file.LastWriteTimeBinary);    // last write time (DateTime.ToBinary, Int64)
        WriteBodyString(w, SecurityDescriptor);
        w.Write(0); w.Write(0);                            // end-of-attributes

        var sendContent = contentObjectIds is not null && contentObjectIds.Contains(file.ObjectId);
        if (!sendContent)
        {
            w.Write((byte)1);                              // "complete, no content"
            w.Write(OpClose);
            w.Write(file.ObjectId);
            return;
        }

        w.Write((byte)0);                                  // "content follows"
        w.Write(OpClose);
        w.Write(file.ObjectId);
        w.Write(OpBeginContent);
        w.Write(file.ObjectId);
        WriteContentChunks(w, file);
    }

    private static void WriteContentChunks(BinaryWriter w, SourceFile file)
    {
        using var content = file.OpenRead();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
        {
            w.Write(OpContentChunk);
            w.Write(read);
            w.Write(buffer, 0, read);
        }

        w.Write(OpContentChunk);
        w.Write(0); // zero-length chunk ends the stream
    }

    private void WriteDirectory(BinaryWriter w, SourceDirectory dir, int parentId, IReadOnlySet<int>? contentObjectIds)
    {
        w.Write(OpDirectory);
        w.Write(dir.ObjectId);
        w.Write(parentId);
        WriteBodyString(w, dir.Name);
        WriteBodyString(w, SecurityDescriptor);
        w.Write(0);                                        // reserved
        w.Write(4); w.Write(DirectoryAttributes);          // attributes (Int32)

        WriteNodes(w, dir.Children, dir.ObjectId, contentObjectIds);

        w.Write(OpClose);
        w.Write(dir.ObjectId);
    }

    /// <summary>Writes a resolver-options blob: base64(gzip(NRBF DeploymentProviderOptions)) as a stream string.</summary>
    private void WriteResolverBlob(BinaryWriter w, string providerName)
    {
        var nrbf = NrbfProviderOptionsWriter.Write(providerName, sourcePath);
        WriteBodyString(w, Convert.ToBase64String(Gzip(nrbf)));
    }

    /// <summary>Writes a stream string: an Int32 byte-length prefix followed by UTF-8 bytes.</summary>
    private static void WriteBodyString(BinaryWriter w, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        w.Write(bytes.Length);
        w.Write(bytes);
    }

    private static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }
}
