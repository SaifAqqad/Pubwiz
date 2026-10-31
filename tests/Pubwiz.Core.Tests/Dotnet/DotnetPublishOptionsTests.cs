using Pubwiz.Core.Dotnet;

namespace Pubwiz.Core.Tests.Dotnet;

public class DotnetPublishOptionsTests
{
    [Fact]
    public void ToArguments_IncludesOnlyTheOptionsThatAreSet()
    {
        var options = new DotnetPublishOptions("/src/Api/Api.csproj") { ProfileName = "Folder" };

        Assert.Equal(["publish", "/src/Api/Api.csproj", "-c", "Release", "-p:PublishProfile=Folder", "-nologo", "-tl:off", "-v:m"], options.ToArguments());
    }

    [Fact]
    public void ToArguments_NeverIncludesSecretProperties()
    {
        var options = new DotnetPublishOptions("Api.csproj")
        {
            TargetFramework = "net10.0",
            RuntimeIdentifier = "linux-x64",
            OutputDirectory = "out",
            Properties = new Dictionary<string, string> { ["SelfContained"] = "true" },
            SecretProperties = new Dictionary<string, string> { ["Password"] = "secret" },
            Verbose = true
        };

        var arguments = options.ToArguments();

        Assert.Contains("-p:SelfContained=true", arguments);
        Assert.Contains("-v:n", arguments);
        Assert.Equal("linux-x64", arguments[arguments.ToList().IndexOf("-r") + 1]);
        Assert.DoesNotContain(arguments, a => a.Contains("secret"));
    }
}
