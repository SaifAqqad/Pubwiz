using System.Text;
using Pubwiz.WebDeploy.Protocol;

namespace Pubwiz.WebDeploy.Tests;

/// <summary>
/// Verifies the object-stream serializer against real captures. The provider scaffold and the
/// gzip'd resolver blobs are not byte-comparable (gzip output differs by implementation), so these
/// tests check the part that encodes the actual folder: the dirPath children region, which the
/// serializer must reproduce exactly for the server to accept and apply the sync.
/// </summary>
public class MsDeployBodyWriterTests
{
    public static TheoryData<string> Captures => new() { "cap_s1", "cap_s2", "cap_s3", "cap_s4", "run1", "update-file" };

    [Theory]
    [MemberData(nameof(Captures))]
    public void MetadataPass_ChildrenRegion_MatchesCapture(string name)
    {
        var body = Fixtures.Read($"{name}.pass1.bin");
        var (tree, _) = CaptureTree.Parse(body);

        var produced = new MsDeployBodyWriter("C:\\src", "MACHINE").WriteMetadataPass(tree);

        Assert.Equal(ChildrenRegion(body), ChildrenRegion(produced));
    }

    [Theory]
    [MemberData(nameof(Captures))]
    public void ContentPass_ChildrenRegion_MatchesCapture(string name)
    {
        var body = Fixtures.Read($"{name}.pass2.bin");
        var (tree, contentIds) = CaptureTree.Parse(body);

        var produced = new MsDeployBodyWriter("C:\\src", "MACHINE").WriteContentPass(tree, contentIds);

        Assert.Equal(ChildrenRegion(body), ChildrenRegion(produced));
    }

    /// <summary>The bytes from the "enter dirPath children" token (03 00 03 00 00 00) to the end.</summary>
    private static byte[] ChildrenRegion(byte[] body)
    {
        var token = new byte[] { 3, 0, 3, 0, 0, 0 };
        var start = IndexOf(body, token);
        Assert.True(start >= 0, "children region token not found");
        return body[start..];
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }

            if (match) return i;
        }

        return -1;
    }
}
