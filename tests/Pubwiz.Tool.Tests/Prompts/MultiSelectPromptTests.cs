using Pubwiz.Tool.Prompts;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Pubwiz.Tool.Tests.Prompts;

public class MultiSelectPromptTests
{
    private static readonly string[] Projects = ["Api", "Worker", "Web"];

    private static MultiSelectPrompt<string> CreatePrompt() => new("Pick projects", Projects, name => [name]);

    private static TestConsole CreateConsole()
    {
        var console = new TestConsole().Interactive();
        console.Profile.Height = 30;
        return console;
    }

    [Fact]
    public async Task EnterWithoutSelecting_ReturnsOnlyTheHighlightedItem()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);

        var result = await CreatePrompt().ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Empty(result.Selected);
        Assert.Equal("Worker", result.Highlighted);
    }

    [Fact]
    public async Task SpaceSelectsItemsInListOrder()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.End);
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Home);
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Enter);

        var result = await CreatePrompt().ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Equal(["Api", "Web"], result.Selected);
    }

    [Fact]
    public async Task Escape_Cancels()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.Escape);

        await Assert.ThrowsAsync<OperationCanceledException>(() => CreatePrompt().ShowAsync(console, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnselectableItemsCannotBeSelected()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.A);
        console.Input.PushKey(ConsoleKey.Enter);

        var prompt = new MultiSelectPrompt<string>("Pick projects", Projects, name => [name]) { IsSelectable = name => name != "Worker" };
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Equal(["Api", "Web"], result.Selected);
    }
}
