using Pubwiz.Core.Presets;
using Pubwiz.Tool.Wizard;

namespace Pubwiz.Tool.Tests.Wizard;

public class RecentRunTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DescribeRecent_OneProfile_NamesIt()
    {
        var run = new RecentRun(Now.AddHours(-2), [new PresetProject("/src/Api/Api.csproj", "Production", 1)]);

        Assert.Equal("Api with Production · 2 hours ago", PresetPage.DescribeRecent(run, Now));
    }

    [Fact]
    public void DescribeRecent_SeveralSteps_ShowsTheOrder()
    {
        var run = new RecentRun(Now.AddDays(-1), [
            new PresetProject("/src/Api/Api.csproj", "Production", 1),
            new PresetProject("/src/Worker/Worker.csproj", "Staging", 1),
            new PresetProject("/src/Web/Web.csproj", "Production", 2)
        ]);

        Assert.Equal("Api + Worker → Web · yesterday", PresetPage.DescribeRecent(run, Now));
    }
}
