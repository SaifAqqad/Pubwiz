using Pubwiz.Core.Targets.Azure;
using Pubwiz.Core.Tests.TestHelpers;

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

    [Fact]
    public async Task ListDeploymentSlotsAsync_ReadsTheSlotsSortedByName()
    {
        var az = new FakeAzRunner().Answer("webapp deployment slot list", """
            [{"name":"testing","defaultHostName":"contoso-testing.azurewebsites.net","state":"Stopped"},
             {"name":"contoso/staging","defaultHostName":"contoso-staging.azurewebsites.net","state":"Running"}]
            """);

        var slots = await az.CreateAzCli().ListDeploymentSlotsAsync("rg", "contoso", TestContext.Current.CancellationToken);

        // The slot's name alone, also when az gives the resource's name (app/slot).
        Assert.Equal(["staging", "testing"], slots.Select(s => s.Name));
        Assert.Equal("https://contoso-staging.scm.azurewebsites.net/", slots[0].ScmUrl.AbsoluteUri);
        Assert.False(slots[0].IsProduction);
        Assert.Contains("--resource-group rg --name contoso", Assert.Single(az.Commands));
    }

    [Fact]
    public async Task ListWebAppsWithSlotsAsync_PutsEachSlotWithItsApp()
    {
        // Resource Graph rows of apps and slots, in any order; the resource group comes from the ID.
        var az = new FakeAzRunner().Answer("rest --method post", """
            { "data": [
              { "id": "/subscriptions/sub-1/resourceGroups/Shop/providers/Microsoft.Web/sites/contoso/slots/staging", "name": "contoso/staging",
                "kind": "app", "defaultHostName": "contoso-staging.azurewebsites.net", "state": "Stopped" },
              { "id": "/subscriptions/sub-1/resourceGroups/Shop/providers/Microsoft.Web/sites/contoso", "name": "contoso",
                "kind": "app,linux", "defaultHostName": "contoso.azurewebsites.net", "state": "Running" },
              { "id": "/subscriptions/sub-1/resourceGroups/Shop/providers/Microsoft.Web/sites/api", "name": "api",
                "kind": "functionapp", "defaultHostName": "api.azurewebsites.net", "state": "" }
            ] }
            """);

        var apps = await az.CreateAzCli().ListWebAppsWithSlotsAsync("sub-1", TestContext.Current.CancellationToken);

        Assert.Equal(["api", "contoso"], apps.Select(a => a.Name));
        Assert.Empty(apps[0].Slots!);
        Assert.Null(apps[0].State);

        var slot = Assert.Single(apps[1].Slots!);
        Assert.Equal(new AzureDeploymentSlot("staging", "contoso-staging.azurewebsites.net", "Stopped"), slot);
        Assert.Equal("Shop", apps[1].ResourceGroup);
        Assert.True(apps[1].IsLinux);

        // The query goes to Resource Graph for the subscription, in a file rather than on the command line.
        Assert.Contains("--body @", Assert.Single(az.Commands));
        var body = System.Text.Json.JsonDocument.Parse(Assert.Single(az.RequestBodies)).RootElement;
        Assert.Equal("sub-1", body.GetProperty("subscriptions")[0].GetString());
        Assert.Contains("microsoft.web/sites/slots", body.GetProperty("query").GetString());
    }

    [Fact]
    public async Task QueryResourceGraphAsync_ReadsEveryPage()
    {
        var az = new FakeAzRunner()
            .AnswerOnce("rest", """{ "data": [ { "id": "1" } ], "$skipToken": "page-2" }""")
            .AnswerOnce("rest", """{ "data": [ { "id": "2" } ] }""");

        var rows = await az.CreateAzCli().QueryResourceGraphAsync<IdRow>("sub-1", "resources", TestContext.Current.CancellationToken);

        Assert.Equal(["1", "2"], rows.Select(r => r.Id));
        Assert.DoesNotContain("skipToken", az.RequestBodies[0]);
        Assert.Contains("\"$skipToken\":\"page-2\"", az.RequestBodies[1]);
    }

    private sealed record IdRow([property: System.Text.Json.Serialization.JsonPropertyName("id")] string Id);

    [Fact]
    public void Production_IsTheAppItself()
    {
        var app = new AzureWebApp("contoso", "rg", "app,linux", "contoso.azurewebsites.net", "Running");

        var production = AzureDeploymentSlot.Production(app);

        Assert.True(production.IsProduction);
        Assert.Equal("production", production.Name);
        Assert.Equal(app.ScmUrl, production.ScmUrl);
    }

    [Fact]
    public async Task SetWebAppImageAsync_UpdatesTheSlot()
    {
        var az = new FakeAzRunner().Answer("webapp config container set", string.Empty);

        await az.CreateAzCli().SetWebAppImageAsync(new AzureAppReference("sub-1", "rg", "contoso", "staging"), "contoso.azurecr.io/api:1", TestContext.Current.CancellationToken);

        var command = Assert.Single(az.Commands);
        Assert.Contains("--name contoso", command);
        Assert.Contains("--slot staging", command);
        Assert.Contains("--subscription sub-1", command);
    }
}
