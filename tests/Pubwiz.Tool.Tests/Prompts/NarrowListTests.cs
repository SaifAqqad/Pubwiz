using Pubwiz.Tool.Prompts;
using Spectre.Console;
using Spectre.Console.Testing;
using System.Text.RegularExpressions;

namespace Pubwiz.Tool.Tests.Prompts;

/// <summary>Lists on a narrow terminal: a row that wraps pushes the list off screen, so every line must fit.</summary>
public partial class NarrowListTests
{
    private const int Width = 40;

    private static readonly string[][] Apps =
    [
        ["contoso-orders-api-production", "rg-contoso-orders-production", "Linux", "Running"],
        ["fabrikam-web", "rg-web", "Windows", "Stopped"]
    ];

    private static TestConsole CreateConsole()
    {
        // With ANSI codes on, the cursor moves between redraws show where each line ends.
        var console = new TestConsole().Interactive().Width(Width).EmitAnsiSequences();
        console.Profile.Height = 30;
        return console;
    }

    [Fact]
    public async Task ChooseWithRefresh_CutsColumnsToTheTerminalWidth()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.Enter);

        var answer = await console.ChooseWithRefreshAsync("App Service app", "Loading...",
            _ => Task.FromResult<IReadOnlyList<string[]>>(Apps), app => app, emptyMessage: "None", TestContext.Current.CancellationToken);

        Assert.Equal(Apps[0], answer);
        AssertFits(console.Output);
        Assert.Contains("contoso-orders-api-production", console.Output);
    }

    [Fact]
    public async Task MultiSelect_CutsColumnsToTheTerminalWidth()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.Enter);

        var prompt = new MultiSelectPrompt<string[]>("Which projects?", Apps, app => app)
        {
            ExitLabel = "Exit",
            Hint = "Select several projects to publish them as a batch, or press enter to publish only the highlighted one."
        };
        await prompt.ShowAsync(console, TestContext.Current.CancellationToken);

        // The hint may wrap; every other line must fit.
        AssertFits(console.Output.Replace(prompt.Hint, string.Empty));
    }

    [Fact]
    public async Task Order_CutsColumnsAndKeysToTheTerminalWidth()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.Enter);

        // Short names, so the plan line under the list ("All 2 at the same time") fits too.
        string[][] rows = [["Api", "contoso-orders-api-production (staging) - Zip Deploy (ZipDeploy)"], ["Web", "fabrikam-web - Zip Deploy (ZipDeploy)"]];
        await new OrderPrompt<string[]>("In what order?", rows, row => row).ShowAsync(console, TestContext.Current.CancellationToken);

        AssertFits(console.Output);
    }

    [Fact]
    public void Choose_CutsLongItemsToTheTerminalWidth()
    {
        var console = CreateConsole();
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);

        // The short item is picked, so the answer written back fits too.
        var answer = console.Choose("Profile", ["contoso-orders-api-production - Zip Deploy [dim](ZipDeploy)[/]", "Back"], item => item);

        Assert.Equal("Back", answer);
        AssertFits(console.Output);
        Assert.Contains("contoso-orders-api-production - Zi…", AnsiEscape().Replace(console.Output, string.Empty));
    }

    private static void AssertFits(string output)
    {
        // The live display moves the cursor between redraws without a new line, so the escape codes also split lines.
        foreach (var line in AnsiEscape().Split(output).SelectMany(part => part.Split('\n')))
        {
            Assert.True(line.TrimEnd().Length <= Width, $"Too wide ({line.TrimEnd().Length}): '{line}'");
        }
    }

    [GeneratedRegex(@"\x1b\[[0-9;?]*[A-Za-z]")]
    private static partial Regex AnsiEscape();
}
