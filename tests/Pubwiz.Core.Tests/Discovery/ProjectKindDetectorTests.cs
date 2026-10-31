using Pubwiz.Core.Discovery;
using Pubwiz.Core.Dotnet;

namespace Pubwiz.Core.Tests.Discovery;

public class ProjectKindDetectorTests
{
    [Theory]
    [InlineData("""{"UsingMicrosoftNETSdkWeb":"true","OutputType":"Exe"}""", ProjectKind.Web)]
    [InlineData("""{"UsingMicrosoftNETSdkBlazorWebAssembly":"true","OutputType":"Exe"}""", ProjectKind.BlazorWasm)]
    [InlineData("""{"UsingMicrosoftNETSdkWorker":"true","OutputType":"Exe"}""", ProjectKind.Worker)]
    [InlineData("""{"AzureFunctionsVersion":"v4","OutputType":"Exe"}""", ProjectKind.AzureFunctions)]
    [InlineData("""{"UseMaui":"true","OutputType":"Exe"}""", ProjectKind.Maui)]
    [InlineData("""{"UseWPF":"true","OutputType":"WinExe"}""", ProjectKind.Desktop)]
    [InlineData("""{"IsAspireHost":"true","OutputType":"Exe"}""", ProjectKind.AspireHost)]
    [InlineData("""{"IsTestProject":"true","OutputType":"Exe"}""", ProjectKind.Test)]
    [InlineData("""{"OutputType":"Exe"}""", ProjectKind.Console)]
    [InlineData("""{"OutputType":"Library"}""", ProjectKind.Library)]
    [InlineData("""{}""", ProjectKind.Unknown)]
    public void Detect_UsesSdkFlagsAndOutputType(string properties, ProjectKind expected)
    {
        var evaluation = DotnetCli.ParseEvaluation($$"""{"Properties":{{properties}}}""");

        Assert.Equal(expected, ProjectKindDetector.Detect(evaluation));
    }

    [Fact]
    public void Detect_RecognizesTestProjectsByPackageBeforeRestore()
    {
        var evaluation = DotnetCli.ParseEvaluation("""
            {
              "Properties": { "OutputType": "Library", "IsTestProject": "" },
              "Items": { "PackageReference": [ { "Identity": "xunit.v3", "Version": "4.0.2" } ] }
            }
            """);

        Assert.Equal(ProjectKind.Test, ProjectKindDetector.Detect(evaluation));
    }
}
