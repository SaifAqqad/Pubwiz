using Pubwiz.Tool.Rendering;

namespace Pubwiz.Tool.Tests.Rendering;

public class DisplayTextTests
{
    [Theory]
    [InlineData("short", 10, "short")]
    [InlineData("exactly10!", 10, "exactly10!")]
    [InlineData("this is too long", 8, "this is…")]
    [InlineData("anything", 1, "")]
    public void Truncate_CutsWithAnEllipsis(string text, int max, string expected)
    {
        Assert.Equal(expected, DisplayText.Truncate(text, max));
    }

    [Fact]
    public void ShortPath_IsRelativeInsideTheCurrentFolder()
    {
        var inside = Path.Combine(Directory.GetCurrentDirectory(), "bin", "publish");

        Assert.Equal(Path.Combine("bin", "publish"), DisplayText.ShortPath(inside));
    }

    [Fact]
    public void ShortPath_LeavesUrlsAlone()
    {
        Assert.Equal("https://contoso.azurewebsites.net", DisplayText.ShortPath("https://contoso.azurewebsites.net"));
    }
}
