using Pubwiz.Tool.Prompts;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Pubwiz.Tool.Tests.Prompts;

public class MultiSelectPromptTests
{
    private static readonly string[] Projects = ["Api", "Worker", "Web"];

    private static MultiSelectPrompt<string> CreatePrompt() => new("Pick projects", Projects, name => [name]);

    private static readonly ConsoleKeyInfo CtrlA = new('\u0001', ConsoleKey.A, shift: false, alt: false, control: true);

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

        Assert.NotNull(result);
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

        Assert.Equal(["Api", "Web"], result!.Selected);
    }

    private static MultiSelectPrompt<string> CreatePromptWithAction() =>
        new("Pick projects", ["↻ Repeat last publish", .. Projects], name => [name, "detail"]) { IsAction = name => name.StartsWith('↻') };

    [Fact]
    public async Task ActionRow_IsHighlightedFirstAndEnterChoosesIt()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.Enter);

        var result = await CreatePromptWithAction().ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Empty(result!.Selected);
        Assert.Equal("↻ Repeat last publish", result.Highlighted);
        Assert.Contains("↻ Repeat last publish", console.Output);
    }

    [Fact]
    public async Task ActionRow_IsNeverSelected()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(CtrlA);
        console.Input.PushKey(ConsoleKey.Enter);

        var result = await CreatePromptWithAction().ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Equal(Projects, result!.Selected);
    }

    [Fact]
    public async Task ActionRow_IsHiddenWhileSearching()
    {
        var console = CreateConsole();
        console.Input.PushText("wo");
        console.Input.PushKey(ConsoleKey.Enter);

        var result = await CreatePromptWithAction().ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Equal("Worker", result!.Highlighted);
    }

    [Fact]
    public async Task Escape_Cancels()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.Escape);

        await Assert.ThrowsAsync<PromptCancelledException>(() => CreatePrompt().ShowAsync(console, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnselectableItemsCannotBeSelected()
    {
        var console = CreateConsole();
        console.Input.PushKey(CtrlA);
        console.Input.PushKey(ConsoleKey.Enter);

        var prompt = new MultiSelectPrompt<string>("Pick projects", Projects, name => [name]) { IsSelectable = name => name != "Worker" };
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Equal(["Api", "Web"], result!.Selected);
    }

    [Fact]
    public async Task Typing_SearchesAndEnterPicksTheFirstMatch()
    {
        var console = CreateConsole();
        console.Input.PushText("we");
        console.Input.PushKey(ConsoleKey.Enter);

        var result = await CreatePrompt().ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Equal("Web", result!.Highlighted);
    }

    [Fact]
    public async Task EscapeClearsTheSearchBeforeCancelling()
    {
        var console = CreateConsole();
        console.Input.PushText("web");
        console.Input.PushKey(ConsoleKey.Escape);
        console.Input.PushKey(ConsoleKey.Enter);

        var result = await CreatePrompt().ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Equal("Api", result!.Highlighted);
    }

    [Fact]
    public async Task ExitRow_ReturnsNull()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.UpArrow); // wraps to the last row: Exit
        console.Input.PushKey(ConsoleKey.Enter);

        var prompt = new MultiSelectPrompt<string>("Pick projects", Projects, name => [name]) { ExitLabel = "Exit" };
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Contains("Exit", console.Output);
    }

    [Fact]
    public async Task HeaderUpdate_IsDrawnWhileTheListIsOpen()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.Enter);
        var prompt = new MultiSelectPrompt<string>("Pick projects", Projects, name => [name])
        {
            Header = "Git main",
            HeaderUpdate = Task.FromResult<string?>("Git main · 3 commits to pull")
        };

        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Equal("Api", result?.Highlighted);
        Assert.Contains("3 commits to pull", console.Output);
    }

    [Fact]
    public async Task SelectedItems_AreShownAboveTheList()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Spacebar); // Worker
        console.Input.PushKey(ConsoleKey.Enter);

        var result = await CreatePrompt().ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Equal(["Worker"], result!.Selected);
        Assert.Equal("Web", result.Highlighted); // the cursor moved on to the next item
        Assert.Contains("Selected (1)", console.Output);
    }

    [Fact]
    public async Task Tab_MovesToTheSelectionWhereSpaceUnselects()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.Spacebar); // Api
        console.Input.PushKey(ConsoleKey.Spacebar); // Worker
        console.Input.PushKey(ConsoleKey.Tab);
        console.Input.PushKey(ConsoleKey.Spacebar); // unselects Api
        console.Input.PushKey(ConsoleKey.Enter);

        var result = await CreatePrompt().ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Equal(["Worker"], result!.Selected);
        Assert.Equal("Worker", result.Highlighted);
    }

    [Fact]
    public async Task Escape_LeavesTheSelectionBeforeCancelling()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.Spacebar); // Api, the cursor moves to Worker
        console.Input.PushKey(ConsoleKey.Tab);
        console.Input.PushKey(ConsoleKey.Escape);
        console.Input.PushKey(ConsoleKey.Enter);

        var result = await CreatePrompt().ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Equal("Worker", result!.Highlighted);
    }

    [Fact]
    public async Task SelectedSection_IsShownBeforeAnythingIsSelected()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.Enter);

        await CreatePrompt().ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Contains("Selected (0)", console.Output);
        Assert.Contains("Nothing selected yet", console.Output);
    }

    [Fact]
    public async Task ThePlaceholderLineDoesNotWidenTheNameColumn()
    {
        var console = CreateConsole();
        console.Profile.Width = 120;
        console.Input.PushKey(ConsoleKey.Enter);

        await new MultiSelectPrompt<string>("Pick projects", Projects, name => [name, "detail"]).ShowAsync(console, TestContext.Current.CancellationToken);

        // The name column is as wide as "Worker", not as the "Nothing selected yet" line.
        Assert.Matches(@"Api {6}detail", console.Output);
    }

    [Fact]
    public async Task CtrlAThenEnter_ConfirmsEverythingInsteadOfExiting()
    {
        var console = CreateConsole();
        console.Input.PushKey(CtrlA);
        console.Input.PushKey(ConsoleKey.Enter);

        var prompt = new MultiSelectPrompt<string>("Pick projects", Projects, name => [name]) { ExitLabel = "Exit" };
        var result = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Equal(["Api", "Worker", "Web"], result!.Selected);
    }
}
