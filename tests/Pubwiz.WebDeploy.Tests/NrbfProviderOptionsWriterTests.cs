using Pubwiz.WebDeploy.Protocol;

namespace Pubwiz.WebDeploy.Tests;

public class NrbfProviderOptionsWriterTests
{
    [Fact]
    public void Write_MatchesTheCapturedProviderOptionsHeaderByteForByte()
    {
        // hdr-ProviderOptions.bin is the decompressed MSDeploy.ProviderOptions header from a real
        // sync to the "PubwizTest" site (providerName=iisApp, path=PubwizTest, no names/values).
        var expected = Fixtures.Read("hdr-ProviderOptions.bin");

        var actual = NrbfProviderOptionsWriter.Write("iisApp", "PubwizTest");

        Assert.Equal(expected, actual);
    }
}
