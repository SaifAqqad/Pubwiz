using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Tool.Credentials;
using Pubwiz.Tool.Wizard;

namespace Pubwiz.Tool.Tests.Wizard;

public class SelectionTests
{
    [Theory]
    [InlineData("Prod", "PUBWIZ_PASSWORD_PROD")]
    [InlineData("contoso - Web Deploy", "PUBWIZ_PASSWORD_CONTOSO_WEB_DEPLOY")]
    [InlineData("api.staging", "PUBWIZ_PASSWORD_API_STAGING")]
    public void ProfileVariable_IsAValidEnvironmentVariableName(string profileName, string expected)
    {
        Assert.Equal(expected, PasswordProvider.ProfileVariable(new PublishProfile(profileName)));
    }

    [Fact]
    public void FindProject_MatchesByNameOrPath()
    {
        var api = new ProjectInfo { Name = "Api", FilePath = Path.GetFullPath("src/Api/Api.csproj"), Kind = ProjectKind.Web };
        var workspace = new Workspace(null, [api]);

        Assert.Same(api, ArgumentSelection.FindProject(workspace, "api"));
        Assert.Same(api, ArgumentSelection.FindProject(workspace, "src/Api/Api.csproj"));
        Assert.Null(ArgumentSelection.FindProject(workspace, "Worker"));
    }

    [Theory]
    [InlineData("Api", "Api", null)]
    [InlineData("Api:2", "Api", 2)]
    [InlineData("src/Api/Api.csproj:1", "src/Api/Api.csproj", 1)]
    [InlineData(@"C:\src\Api.csproj", @"C:\src\Api.csproj", null)]
    [InlineData(@"C:\src\Api.csproj:3", @"C:\src\Api.csproj", 3)]
    public void ParseProject_ReadsAnOptionalOrderAfterTheLastColon(string value, string project, int? order)
    {
        Assert.Equal((project, order), ArgumentSelection.ParseProject(value));
    }

    [Fact]
    public void ParseProject_RejectsOrderZero()
    {
        Assert.Throws<UserErrorException>(() => ArgumentSelection.ParseProject("Api:0"));
    }
}
