using System.Text;
using Pubwiz.WebDeploy.Protocol;

namespace Pubwiz.WebDeploy.Tests;

/// <summary>The expected values are the checksums msdeploy.exe sent for these exact file contents (-whatif -useCheckSum).</summary>
public class WebDeployChecksumTests
{
    public static TheoryData<byte[], uint> MsDeployChecksums => new()
    {
        { [], 0xD7978EEB },
        { [0x00], 0x7EE8CDCD },
        { [0x00, 0x00], 0x7EE8CDCD },
        { [0x01], 0xD94C8E21 },
        { [0xFF], 0x5D578835 },
        { "a"u8.ToArray(), 0x1C41DFC4 },
        { [0x61, 0x00, 0x00], 0x1C41DFC4 }, // trailing zeros don't count
        { [0x00, 0x61], 0x0AE71B91 },        // leading ones do
        { "ab"u8.ToArray(), 0xF69199F5 },
        { "ba"u8.ToArray(), 0x5BD3CBED },
        { "abc"u8.ToArray(), 0x9A93D48C },
        { "identical\r\n"u8.ToArray(), 0x749EE318 },
        { "local copy!\r\n"u8.ToArray(), 0x1650F2A1 },
        { Repeat('a', 4096), 0xD4F2048B },
        { Repeat('a', 8193), 0xF4A19AF1 },
        { Repeat('a', 65536), 0xCA975130 }
    };

    [Theory]
    [MemberData(nameof(MsDeployChecksums))]
    public void Compute_MatchesMsDeploy(byte[] content, uint expected)
    {
        Assert.Equal(expected, WebDeployChecksum.Compute(content));
    }

    [Fact]
    public void Compute_HasNoChecksumForFilesOver64Kb()
    {
        var large = new SourceFile("big.bin", WebDeployChecksum.MaxSize + 1, 0, () => new MemoryStream(new byte[WebDeployChecksum.MaxSize + 1]));
        var small = new SourceFile("a.txt", 1, 0, () => new MemoryStream("a"u8.ToArray()));

        Assert.Null(WebDeployChecksum.Compute(large));
        Assert.Equal(0x1C41DFC4u, WebDeployChecksum.Compute(small));
    }

    [Fact]
    public void TraceResults_ReadTheServersChecksums()
    {
        // From a checksum sync: object 5 exists on the server (with a checksum), object 6 is new.
        var trace = TraceResults.Parse("""<results><incomplete objectId="5" checksum="3791447339" /><incomplete objectId="6" /></results>""");

        Assert.Equal([5, 6], trace.IncompleteObjectIds.Order());
        Assert.Equal(3791447339u, Assert.Single(trace.ServerChecksums).Value);
    }

    private static byte[] Repeat(char c, int count) => Encoding.ASCII.GetBytes(new string(c, count));
}
