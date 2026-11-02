namespace Pubwiz.WebDeploy.Protocol;

/// <summary>A node in the source folder tree: either a <see cref="SourceFile"/> or a <see cref="SourceDirectory"/>.</summary>
public abstract class SourceNode
{
    protected SourceNode(string name) => Name = name;

    /// <summary>The file or directory name (no path), as it will appear on the server.</summary>
    public string Name { get; }

    /// <summary>The assigned object id in the sync stream (set while serializing).</summary>
    internal int ObjectId { get; set; }
}

/// <summary>A file to deploy, with the metadata Web Deploy puts in the sync stream and a way to read its bytes.</summary>
public sealed class SourceFile : SourceNode
{
    private readonly Func<Stream> openRead;

    public SourceFile(string name, long length, long lastWriteTimeBinary, Func<Stream> openRead) : base(name)
    {
        Length = length;
        LastWriteTimeBinary = lastWriteTimeBinary;
        this.openRead = openRead;
    }

    /// <summary>The file length in bytes.</summary>
    public long Length { get; }

    /// <summary>
    /// The last-write time encoded as <see cref="DateTime.ToBinary"/> of the UTC time. The server
    /// reconstructs it with <c>DateTime.FromBinary</c>, so a Windows <c>FILETIME</c> is rejected.
    /// </summary>
    public long LastWriteTimeBinary { get; }

    /// <summary>Opens the file content for reading (used in the second sync pass).</summary>
    public Stream OpenRead() => openRead();
}

/// <summary>A directory and its (name-sorted) children.</summary>
public sealed class SourceDirectory : SourceNode
{
    public SourceDirectory(string name, IReadOnlyList<SourceNode> children) : base(name) => Children = children;

    /// <summary>The directory's immediate children, files and subdirectories together, in sync order.</summary>
    public IReadOnlyList<SourceNode> Children { get; }

    /// <summary>All files in the tree, depth-first.</summary>
    public IEnumerable<SourceFile> Files()
    {
        foreach (var node in Children)
        {
            if (node is SourceFile file)
            {
                yield return file;
            }
            else if (node is SourceDirectory directory)
            {
                foreach (var nested in directory.Files())
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>
    /// Builds a tree from a folder on disk. Children are sorted by name (case-insensitive ordinal),
    /// files and directories together, which is the order the official client emits.
    /// </summary>
    public static SourceDirectory FromDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        if (!info.Exists)
        {
            throw new DirectoryNotFoundException($"Source folder not found: {path}");
        }

        return Build(info, isRoot: true);
    }

    private static SourceDirectory Build(DirectoryInfo dir, bool isRoot)
    {
        var children = new List<SourceNode>();
        foreach (var entry in dir.GetFileSystemInfos().OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            children.Add(entry switch
            {
                DirectoryInfo sub => Build(sub, isRoot: false),
                FileInfo file => new SourceFile(
                    file.Name,
                    file.Length,
                    file.LastWriteTimeUtc.ToBinary(),
                    () => File.OpenRead(file.FullName)),
                _ => throw new NotSupportedException($"Unsupported entry: {entry.FullName}"),
            });
        }

        // The root keeps the physical folder name only as a placeholder; the server uses the dest path.
        return new SourceDirectory(isRoot ? dir.Name : dir.Name, children);
    }
}
