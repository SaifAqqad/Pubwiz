using Pubwiz.WebDeploy.Protocol;

namespace Pubwiz.WebDeploy.Tests;

public class SourceTreeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pubwiz-wd-" + Guid.NewGuid().ToString("N"));

    public SourceTreeTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void FromDirectory_SortsChildrenByNameWithFilesAndDirectoriesMixed()
    {
        // Web Deploy numbers objects in this order, so the enumeration must match it.
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "index.html"), "x");
        File.WriteAllText(Path.Combine(root, "sub", "a.txt"), "y");
        File.WriteAllText(Path.Combine(root, "readme.txt"), "zz");

        var tree = SourceDirectory.FromDirectory(root);

        Assert.Collection(tree.Children,
            first => Assert.Equal("index.html", first.Name),
            second => Assert.Equal("readme.txt", second.Name),
            third =>
            {
                var dir = Assert.IsType<SourceDirectory>(third);
                Assert.Equal("sub", dir.Name);
                var file = Assert.IsType<SourceFile>(Assert.Single(dir.Children));
                Assert.Equal("a.txt", file.Name);
            });
    }

    [Fact]
    public void FromDirectory_ReadsFileLengthAndLastWriteTimeAsDateTimeBinary()
    {
        var path = Path.Combine(root, "data.bin");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5]);
        var expected = new FileInfo(path).LastWriteTimeUtc.ToBinary();

        var file = Assert.IsType<SourceFile>(Assert.Single(SourceDirectory.FromDirectory(root).Children));

        Assert.Equal(5, file.Length);
        Assert.Equal(expected, file.LastWriteTimeBinary);
    }

    [Fact]
    public void FromDirectory_Throws_WhenFolderMissing()
        => Assert.Throws<DirectoryNotFoundException>(() => SourceDirectory.FromDirectory(Path.Combine(root, "nope")));
}
