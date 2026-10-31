using Pubwiz.Core.Targets.Azure;

namespace Pubwiz.Core.Tests.Targets.Azure;

public class AzCliTests
{
    [Theory]
    [InlineData("ID=ubuntu\nID_LIKE=debian", "apt")]
    [InlineData("ID=linuxmint\nID_LIKE=\"ubuntu debian\"", "apt")]
    [InlineData("ID=fedora", "Fedora package")]
    [InlineData("ID=\"rocky\"\nID_LIKE=\"rhel centos fedora\"", "Microsoft repository")]
    [InlineData("ID=opensuse-tumbleweed\nID_LIKE=\"opensuse suse\"", "zypper")]
    [InlineData("ID=azurelinux", "tdnf")]
    public void ForLinux_PicksThePackageManager(string osRelease, string expectedTool)
    {
        var option = AzCliInstallGuide.ForLinux(osRelease, isImageBased: false, hasHomebrew: false);

        Assert.NotNull(option);
        Assert.Contains(expectedTool, option.Description);
    }

    [Fact]
    public void ForLinux_UsesHomebrewOnImageBasedSystems()
    {
        Assert.Contains("brew", AzCliInstallGuide.ForLinux("ID=bazzite\nID_LIKE=fedora", isImageBased: true, hasHomebrew: true)!.Command);
        Assert.Null(AzCliInstallGuide.ForLinux("ID=bazzite\nID_LIKE=fedora", isImageBased: true, hasHomebrew: false));
    }

    [Fact]
    public void Deserialize_ReadsWebApps()
    {
        var apps = AzCli.Deserialize<List<AzureWebApp>>("""
            [{"name":"contoso","resourceGroup":"rg","kind":"app,linux","defaultHostName":"contoso.azurewebsites.net","state":"Running"}]
            """)!;

        var app = Assert.Single(apps);
        Assert.True(app.IsLinux);
        Assert.False(app.IsFunctionApp);
        Assert.Equal("https://contoso.scm.azurewebsites.net/", app.ScmUrl.AbsoluteUri);
    }
}
