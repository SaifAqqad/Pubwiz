using Pubwiz.Core.Tests.TestHelpers;
using Pubwiz.Core.Transforms;

namespace Pubwiz.Core.Tests.Transforms;

public sealed class WebConfigTransformTests : IDisposable
{
    private const string WebConfig = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <system.webServer>
            <aspNetCore processPath="dotnet" arguments=".\App.dll" stdoutLogEnabled="false">
              <environmentVariables>
                <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Development" />
              </environmentVariables>
            </aspNetCore>
          </system.webServer>
        </configuration>
        """;

    private const string ReleaseTransform = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration xmlns:xdt="http://schemas.microsoft.com/XML-Document-Transform">
          <system.webServer>
            <aspNetCore>
              <environmentVariables>
                <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" xdt:Transform="SetAttributes" xdt:Locator="Match(name)" />
              </environmentVariables>
            </aspNetCore>
          </system.webServer>
        </configuration>
        """;

    private const string StdoutTransform = """
        <configuration xmlns:xdt="http://schemas.microsoft.com/XML-Document-Transform">
          <system.webServer>
            <aspNetCore stdoutLogEnabled="true" xdt:Transform="SetAttributes(stdoutLogEnabled)" />
          </system.webServer>
        </configuration>
        """;

    private readonly TempDirectory _temp = new();

    [Fact]
    public void Plan_FollowsTheSdkOrderAndIgnoresCase()
    {
        _temp.CreateFile("web.config", WebConfig);
        var release = _temp.CreateFile("Web.Release.config", ReleaseTransform);
        var staging = _temp.CreateFile("web.Staging.config", StdoutTransform);
        var profileFile = _temp.CreateFile("Properties/PublishProfiles/Prod.transform", StdoutTransform);

        var plan = WebConfigTransformPlanner.Plan(_temp.Path, "Release", profileName: "Prod", environmentName: "Staging");

        Assert.Equal([TransformKind.Configuration, TransformKind.Environment, TransformKind.ProfileFile], plan.Select(t => t.Kind));
        Assert.Equal([release, staging, profileFile], plan.Select(t => t.FilePath));
        Assert.Equal([false, true, true], plan.Select(t => t.HasExpectedCasing));
    }

    [Fact]
    public void Transform_AppliesTransformsInOrderWithoutTouchingTheSource()
    {
        var source = _temp.CreateFile("web.config", WebConfig);
        var release = _temp.CreateFile("web.Release.config", ReleaseTransform);
        var stdout = _temp.CreateFile("web.Logs.config", StdoutTransform);
        var output = Path.Combine(_temp.Path, "out", "web.config");

        var warnings = WebConfigTransformer.Transform(source, [release, stdout], output);

        var result = File.ReadAllText(output);
        Assert.Contains("value=\"Production\"", result);
        Assert.Contains("stdoutLogEnabled=\"true\"", result);
        Assert.DoesNotContain("xdt:", result);
        Assert.Empty(warnings);
        Assert.Equal(WebConfig, File.ReadAllText(source));
    }

    [Fact]
    public void Transform_ReportsLocatorsThatMatchNothing()
    {
        var source = _temp.CreateFile("web.config", "<configuration><appSettings /></configuration>");
        var transform = _temp.CreateFile("web.Release.config", """
            <configuration xmlns:xdt="http://schemas.microsoft.com/XML-Document-Transform">
              <appSettings>
                <add key="Missing" value="x" xdt:Transform="SetAttributes" xdt:Locator="Match(key)" />
              </appSettings>
            </configuration>
            """);

        var warnings = WebConfigTransformer.Transform(source, [transform], Path.Combine(_temp.Path, "out.config"));

        Assert.NotEmpty(warnings);
    }

    public void Dispose() => _temp.Dispose();
}
