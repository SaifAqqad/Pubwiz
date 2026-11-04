using Pubwiz.Tool.Prompts;
using Spectre.Console.Testing;

namespace Pubwiz.Tool.Tests.Prompts;

public class OrderPromptTests
{
    private static readonly string[] Projects = ["Api", "Worker", "Web"];

    private static OrderPrompt<string> CreatePrompt() => new("In what order?", Projects, name => [name]);

    private static TestConsole CreateConsole()
    {
        var console = new TestConsole().Interactive();
        console.Profile.Height = 30;
        return console;
    }

    [Fact]
    public async Task Enter_KeepsEverythingAtTheSameTime()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.Enter);

        var steps = await CreatePrompt().ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Equal([1, 1, 1], steps);
        Assert.Contains("All 3 at the same time", console.Output);
    }

    [Fact]
    public async Task TypingNumbers_SetsTheSteps()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.End);   // Web
        console.Input.PushText("2");
        console.Input.PushKey(ConsoleKey.Enter);

        var steps = await CreatePrompt().ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Equal([1, 1, 2], steps);
        Assert.Contains("Api and Worker at the same time, then Web", console.Output);
    }

    [Fact]
    public async Task RightArrow_MakesTheProjectLater()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.RightArrow); // Api to 2, moving it after the others
        console.Input.PushKey(ConsoleKey.Enter);

        var steps = await CreatePrompt().ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Equal([2, 1, 1], steps);
    }

    [Fact]
    public async Task O_PublishesOneAfterAnother()
    {
        var console = CreateConsole();
        console.Input.PushText("o");
        console.Input.PushKey(ConsoleKey.Enter);

        var steps = await CreatePrompt().ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Equal([1, 2, 3], steps);
    }

    [Fact]
    public async Task Escape_Cancels()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.Escape);

        await Assert.ThrowsAsync<PromptCancelledException>(() => CreatePrompt().ShowAsync(console, TestContext.Current.CancellationToken));
    }
}
