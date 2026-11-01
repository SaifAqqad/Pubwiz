using Pubwiz.Tool.Prompts;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Pubwiz.Tool.Tests.Prompts;

public class SecretPromptTests
{
    [Fact]
    public async Task ReturnsTheTypedTextAndMasksIt()
    {
        var console = new TestConsole().Interactive();
        console.Input.PushText("s3cret");
        console.Input.PushKey(ConsoleKey.Enter);

        var answer = await new SecretPrompt("Password:").ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Equal("s3cret", answer);
        Assert.DoesNotContain("s3cret", console.Output);
        Assert.Contains("******", console.Output);
    }

    [Fact]
    public async Task BackspaceDeletesAndEmptyEnterIsIgnored()
    {
        var console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushText("abx");
        console.Input.PushKey(ConsoleKey.Backspace);
        console.Input.PushText("c");
        console.Input.PushKey(ConsoleKey.Enter);

        var answer = await new SecretPrompt("Password:").ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Equal("abc", answer);
    }

    [Fact]
    public async Task EscCancels()
    {
        var console = new TestConsole().Interactive();
        console.Input.PushText("abc");
        console.Input.PushKey(ConsoleKey.Escape);

        await Assert.ThrowsAsync<PromptCancelledException>(() =>
            new SecretPrompt("Password:").ShowAsync(console, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AsksAgainWhenValidationFails()
    {
        var console = new TestConsole().Interactive();
        console.Input.PushText("abc");
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushText("abcdef");
        console.Input.PushKey(ConsoleKey.Enter);

        var prompt = new SecretPrompt("Password:") { Validate = p => p.Length >= 6 ? null : "At least 6 characters" };
        var answer = await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        Assert.Equal("abcdef", answer);
        Assert.Contains("At least 6 characters", console.Output);
    }
}
