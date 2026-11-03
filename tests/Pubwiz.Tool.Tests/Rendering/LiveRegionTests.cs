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
}
