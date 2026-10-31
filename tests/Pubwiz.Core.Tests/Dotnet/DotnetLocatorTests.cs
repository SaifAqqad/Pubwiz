using Pubwiz.Core.Dotnet;

namespace Pubwiz.Core.Tests.Dotnet;

public class DotnetLocatorTests
{
    [Fact]
    public void Find_ReturnsAnExistingExecutable()
    {
        // The tests themselves run on dotnet, so it can always be found.
        var path = DotnetLocator.Find();

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
    }
}
