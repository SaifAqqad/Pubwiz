using Pubwiz.Tool.Prompts;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Pubwiz.Tool.Tests.Prompts;

public class ChooseWithRefreshTests
{
    [Fact]
    public async Task RefreshLoadsTheListAgain()
    {
        var console = new TestConsole().Interactive();
        var loads = 0;

        // Load 1: [Api]; pick Refresh (the last item). Load 2: [Api, Web]; pick Web.
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);

        var answer = await console.ChooseWithRefreshAsync("App", "Loading...", _ =>
            {
                loads++;
                return Task.FromResult<IReadOnlyList<string>>(loads == 1 ? ["Api"] : ["Api", "Web"]);
            },
            name => [name], emptyMessage: "None", TestContext.Current.CancellationToken);

        Assert.Equal("Web", answer);
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task AnEmptyListOffersRefresh()
    {
        var console = new TestConsole().Interactive();
        var loads = 0;
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);

        var answer = await console.ChooseWithRefreshAsync("App", "Loading...", _ =>
            {
                loads++;
                return Task.FromResult<IReadOnlyList<string>>(loads == 1 ? [] : ["Api"]);
            },
            name => [name], emptyMessage: "No apps yet", TestContext.Current.CancellationToken);

        Assert.Equal("Api", answer);
        Assert.Contains("No apps yet", console.Output);
    }

    [Fact]
    public async Task SearchPicksPrefixMatchesFirst()
    {
        var console = new TestConsole().Interactive();
        console.Input.PushText("con");
        console.Input.PushKey(ConsoleKey.Enter);

        var answer = await console.ChooseWithRefreshAsync("App", "Loading...",
            _ => Task.FromResult<IReadOnlyList<string>>(["acme-contoso", "contoso"]),
            name => [name], emptyMessage: "None", TestContext.Current.CancellationToken);

        Assert.Equal("contoso", answer);
    }

    [Fact]
    public async Task EscGoesBack()
    {
        var console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.Escape);

        await Assert.ThrowsAsync<PromptCancelledException>(() => console.ChooseWithRefreshAsync("App", "Loading...",
            _ => Task.FromResult<IReadOnlyList<string>>(["Api"]),
            name => [name], emptyMessage: "None", TestContext.Current.CancellationToken));
    }
}
