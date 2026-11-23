using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Targets.Azure;
using Pubwiz.Tool.Azure;

namespace Pubwiz.Tool.Tests.Azure;

public class AzureAppListTests
{
    /// <summary>Resource Graph: contoso with a staging slot, and fabrikam without slots.</summary>
    private const string ResourceGraph = """
        { "data": [
          { "id": "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Web/sites/contoso", "name": "contoso", "kind": "app", "defaultHostName": "contoso.azurewebsites.net" },
          { "id": "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Web/sites/contoso/slots/staging", "name": "contoso/staging", "kind": "app", "defaultHostName": "contoso-staging.azurewebsites.net" },
          { "id": "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Web/sites/fabrikam", "name": "fabrikam", "kind": "app", "defaultHostName": "fabrikam.azurewebsites.net" }
        ] }
        """;

    /// <summary>App Service itself: the same apps, without their slots.</summary>
    private const string WebApps = """
        [{"name":"contoso","resourceGroup":"rg","kind":"app","defaultHostName":"contoso.azurewebsites.net"},
         {"name":"fabrikam","resourceGroup":"rg","kind":"app","defaultHostName":"fabrikam.azurewebsites.net"}]
        """;

    private static (AzureAppList List, FakeRunner Az) Create(bool resourceGraphWorks = true)
    {
        var runner = new FakeRunner()
            .Answer("webapp list", WebApps)
            .Answer("functionapp list", "[]")
            .Answer("webapp deployment slot list", """[{"name":"staging","defaultHostName":"contoso-staging.azurewebsites.net"}]""");
        if (resourceGraphWorks)
        {
            runner.Answer("rest", ResourceGraph);
        }

        return (new AzureAppList(new AzCli(runner, "az")), runner);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheAppsAreLoadedOncePerRun_WithOneResourceGraphQuery()
    {
        var (list, az) = Create();

        await list.GetAppsAsync("sub-1", Token);
        var apps = await list.GetAppsAsync("sub-1", Token);

        Assert.Equal(["contoso", "fabrikam"], apps.Select(a => a.Name));
        Assert.Equal(["rest"], az.Commands.Select(c => c.Split(' ')[0]));
    }

    [Fact]
    public async Task ChoosingASlot_NeedsNoMoreAzCalls()
    {
        var (list, az) = Create();
        var apps = await list.GetAppsAsync("sub-1", Token);

        var contosoSlots = await list.GetSlotsAsync(apps[0], Token);
        var fabrikamSlots = await list.GetSlotsAsync(apps[1], Token);

        Assert.Equal("staging", Assert.Single(contosoSlots).Name);
        Assert.Empty(fabrikamSlots);
        Assert.Single(az.Commands);
    }

    [Fact]
    public async Task Refresh_AsksAppServiceItself_AndTheChosenAppsSlotsOnce()
    {
        var (list, az) = Create();
        await list.GetAppsAsync("sub-1", Token);

        // Resource Graph may not have an app or slot created moments ago yet.
        var contoso = (await list.LoadAppsAsync("sub-1", Token))[0];
        Assert.Null(contoso.Slots);

        await list.GetSlotsAsync(contoso, Token);
        var slots = await list.GetSlotsAsync(contoso, Token);

        Assert.Equal("staging", Assert.Single(slots).Name);
        Assert.Single(az.Commands, c => c.StartsWith("webapp list"));
        Assert.Single(az.Commands, c => c.StartsWith("webapp deployment slot list"));
    }

    [Fact]
    public async Task WithoutResourceGraph_AppServiceIsAsked()
    {
        var (list, az) = Create(resourceGraphWorks: false);

        var apps = await list.GetAppsAsync("sub-1", Token);

        Assert.Equal(["contoso", "fabrikam"], apps.Select(a => a.Name));
        Assert.Contains(az.Commands, c => c.StartsWith("webapp list"));
    }

    /// <summary>Answers az commands with fixed output, matched by how the command starts; anything else fails.</summary>
    private sealed class FakeRunner : IProcessRunner
    {
        private readonly List<(string Command, string Output)> _answers = [];

        public List<string> Commands { get; } = [];

        public FakeRunner Answer(string command, string output)
        {
            _answers.Add((command, output));
            return this;
        }

        public Task<ProcessResult> RunAsync(ProcessSpec spec, Action<ProcessOutputLine>? onOutput = null, CancellationToken cancellationToken = default)
        {
            var command = string.Join(' ', spec.Arguments);
            lock (Commands)
            {
                Commands.Add(command);
            }

            var answer = _answers.FirstOrDefault(a => command.StartsWith(a.Command, StringComparison.Ordinal));
            return Task.FromResult(answer.Output is null ? new ProcessResult(1, string.Empty, "no answer") : new ProcessResult(0, answer.Output, string.Empty));
        }

        public Task<int> RunInteractiveAsync(ProcessSpec spec, CancellationToken cancellationToken = default) => Task.FromResult(1);
    }
}
