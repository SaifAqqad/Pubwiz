using Pubwiz.Tool.Infrastructure;

namespace Pubwiz.Tool.Tests.Infrastructure;

public class UserPathTests
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Theory]
    [InlineData("~/Desktop")]
    [InlineData("~\\Desktop")]
    [InlineData("\"~/Desktop\"")]
    public void ExpandsTheHomeFolder(string typed)
    {
        Assert.Equal(Path.Combine(Home, "Desktop"), UserPath.ToFullPath(typed));
    }

    [Fact]
    public void TildeAloneIsTheHomeFolder() => Assert.Equal(Home, UserPath.ToFullPath("~"));

    [Fact]
    public void RelativePathsUseTheCurrentFolder()
    {
        Assert.Equal(Path.Combine(Directory.GetCurrentDirectory(), "logs"), UserPath.ToFullPath(" logs "));
    }

    [Fact]
    public void ATildeInsideANameIsKept()
    {
        Assert.Equal(Path.Combine(Directory.GetCurrentDirectory(), "a~b"), UserPath.ToFullPath("a~b"));
    }

    [Fact]
    public void UnescapesDraggedPathsOnMacAndLinux()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows paths use backslashes as separators.");
        }

        Assert.Equal("/tmp/My Folder/a(1).pubxml", UserPath.ToFullPath(@"/tmp/My\ Folder/a\(1\).pubxml"));
    }
}
