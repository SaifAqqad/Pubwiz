using Pubwiz.Tool.Rendering;
using Spectre.Console;

namespace Pubwiz.Tool.Tests.Rendering;

public sealed class LiveRegionTests
{
    private readonly StringWriter _output = new();
    private readonly IAnsiConsole _console;

    public LiveRegionTests()
    {
        _console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.Yes,
            Out = new AnsiConsoleOutput(_output)
        });
        _console.Profile.Width = 40;
    }

    [Fact]
    public void RewritesOnlyTheLinesThatChanged()
    {
        using var region = new LiveRegion(_console);
        region.Update(["first", "second", "third"]);
        _output.GetStringBuilder().Clear();

        region.Update(["first", "changed", "third"]);
        var frame = _output.ToString();

        Assert.Contains("changed", frame);
        Assert.DoesNotContain("first", frame);
        Assert.DoesNotContain("third", frame);
        Assert.StartsWith("\u001b[?2026h\r\u001b[3A", frame);
    }

    [Fact]
    public void ClearsWhatsLeftWhenTheBlockGetsShorter()
    {
        using var region = new LiveRegion(_console);
        region.Update(["one", "two", "three"]);
        _output.GetStringBuilder().Clear();

        region.Update(["one"]);

        Assert.EndsWith("\u001b[J\u001b[?2026l", _output.ToString());
    }

    [Fact]
    public void RemovesTheBlockAndShowsTheCursorWhenDone()
    {
        var region = new LiveRegion(_console);
        region.Update(["one", "two"]);
        _output.GetStringBuilder().Clear();

        region.Dispose();

        Assert.Equal("\r\u001b[2A\u001b[J\u001b[?25h", _output.ToString());
    }

    [Fact]
    public void ASpinnerTickDrawsOnlyTheSpinner()
    {
        using var region = new LiveRegion(_console);
        // As in the dashboard, the spinner is styled, so it's a piece of its own.
        var line = $"  [bold]{LiveRegion.Spinner}[/] Build and publish  [dim]Compiling[/]";
        region.Update([line], "a");
        _output.GetStringBuilder().Clear();

        region.Update([line], "b");
        var frame = _output.ToString();

        // Moves to column 3 and draws the new frame; the rest of the line stays as it is.
        Assert.Contains("\u001b[3G\u001b[1mb", frame);
        Assert.DoesNotContain("Build", frame);
        Assert.DoesNotContain("\u001b[2K", frame);
    }

    [Fact]
    public void TextThatChangesWidthIsRewrittenFromThereAndTheRestCleared()
    {
        using var region = new LiveRegion(_console);
        region.Update(["[bold]Api[/] [dim]Compiling a long file name[/]"]);
        _output.GetStringBuilder().Clear();

        region.Update(["[bold]Api[/] [dim]Linking[/]"]);
        var frame = _output.ToString();

        Assert.Contains("\u001b[5G", frame);
        Assert.Contains("Linking", frame);
        Assert.Contains("\u001b[K", frame);
        Assert.DoesNotContain("Api", frame);
    }

    [Fact]
    public void Split_GivesEachStyledPieceItsColumnAndWidth()
    {
        var runs = LiveRegion.Run.Split("\u001b[1mApi\u001b[0m Prod \u001b[2m12s\u001b[0m");

        Assert.NotNull(runs);
        Assert.Equal([(0, 3), (3, 6), (9, 3)], runs.Select(r => (r.Column, r.Width)));
    }

    [Theory]
    [InlineData("\u001b[1m構築\u001b[0m")] // wide characters
    [InlineData("مشروع")] // right to left
    [InlineData("\u001b]8;;https://example.com\u001b\\link\u001b]8;;\u001b\\")] // a link, not a colour code
    public void Split_LeavesLinesItCantMeasureWhole(string ansi)
    {
        Assert.Null(LiveRegion.Run.Split(ansi));
    }
}
