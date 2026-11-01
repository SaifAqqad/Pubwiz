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

    [Fact]
    public void Link_PointsAtTheFullPathAndShowsTheShortOne()
    {
        var inside = Path.Combine(Directory.GetCurrentDirectory(), "bin", "my publish");

        var markup = DisplayText.Link(inside);

        Assert.Equal($"[link={new Uri(inside).AbsoluteUri}]{Path.Combine("bin", "my publish")}[/]", markup);
        Assert.DoesNotContain(" ", DisplayText.ToLinkUrl(inside));
    }

    [Fact]
    public void Link_EscapesBracketsInTheTextAndUrl()
    {
        var path = Path.Combine(Path.GetTempPath(), "[x]");

        var markup = DisplayText.Link(path, text: "[x]");

        Assert.EndsWith("][[x]][/]", markup);
        Assert.DoesNotContain("[x]", DisplayText.ToLinkUrl(path));
    }

    [Fact]
    public void Link_LinksWebUrlsAndLeavesOtherTextPlain()
    {
        Assert.Equal("[link=https://contoso.azurewebsites.net/]https://contoso.azurewebsites.net[/]", DisplayText.Link("https://contoso.azurewebsites.net"));
        Assert.Equal("relative/path", DisplayText.Link("relative/path"));
    }

    [Theory]
    [InlineData(0.5, "just now")]
    [InlineData(5, "5 minutes ago")]
    [InlineData(61, "1 hour ago")]
    [InlineData(60 * 30, "yesterday")]
    [InlineData(60 * 24 * 4, "4 days ago")]
    [InlineData(60 * 24 * 21, "3 weeks ago")]
    [InlineData(60 * 24 * 400, "1 year ago")]
    public void RelativeTime_RoundsDown(double minutesAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(expected, DisplayText.RelativeTime(now.AddMinutes(-minutesAgo), now));
    }
}

