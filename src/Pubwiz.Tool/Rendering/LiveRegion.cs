using System.Text;

namespace Pubwiz.Tool.Rendering;

/// <summary>
/// A block of lines drawn at the cursor and redrawn in place, rewriting only the lines that changed.
/// </summary>
/// <remarks>
/// Spectre.Console's live display rewrites every line, padded to the terminal's width, on each refresh. With a batch
/// of projects that's several KB ten times a second, which makes the terminal lag while the builds keep the machine
/// busy. Here a spinner tick only rewrites the lines it's on. Each frame is also wrapped in synchronized-output markers
/// (DEC mode 2026), so terminals that support them show it at once instead of line by line; others ignore them.
/// <para>
/// The lines are markup, and each must fit on one terminal line (no wrapping), with the block shorter than the
/// terminal, so the cursor can always move back to its first line.
/// </para>
/// </remarks>
internal sealed class LiveRegion : IDisposable
{
    private const string Csi = "\u001b[";
    private const string BeginFrame = Csi + "?2026h";
    private const string EndFrame = Csi + "?2026l";
    private const string ClearLine = Csi + "2K";
    private const string ClearBelow = Csi + "J";

    private readonly IAnsiConsole _console;
    private readonly StringWriter _ansi = new();
    private readonly IAnsiConsole _renderer;

    /// <summary>The markup of the lines on screen, to tell which ones changed.</summary>
    private List<string> _lines = [];

    private int _width;

    public LiveRegion(IAnsiConsole console)
    {
        _console = console;

        // Turns markup into escape codes with the terminal's colours, into a string instead of the terminal.
        _renderer = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = (ColorSystemSupport)console.Profile.Capabilities.ColorSystem,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(_ansi)
        });

        Write(Csi + "?25l"); // hide the cursor
    }

    /// <summary>Draws <paramref name="lines"/> over the previous frame.</summary>
    public void Update(IReadOnlyList<string> lines)
    {
        // A resized terminal may have rewrapped the old lines, so the whole block is drawn again.
        var width = _console.Profile.Width;
        var redrawAll = width != _width;
        _width = width;
        _renderer.Profile.Width = width;

        var frame = new StringBuilder(BeginFrame);
        frame.Append(MoveToFirstLine());
        if (redrawAll)
        {
            frame.Append(ClearBelow);
        }

        for (var i = 0; i < lines.Count; i++)
        {
            if (redrawAll || i >= _lines.Count || lines[i] != _lines[i])
            {
                frame.Append(ClearLine).Append(ToAnsi(lines[i]));
            }

            frame.Append("\r\n");
        }

        // The new frame is shorter: clear what's left of the old one.
        if (lines.Count < _lines.Count)
        {
            frame.Append(ClearBelow);
        }

        frame.Append(EndFrame);
        Write(frame.ToString());
        _lines = [.. lines];
    }

    /// <summary>Removes the block and shows the cursor again, leaving the cursor where the block started.</summary>
    public void Dispose()
    {
        Write(MoveToFirstLine() + ClearBelow + Csi + "?25h");
        _lines = [];
    }

    /// <summary>The cursor sits at the start of the line below the block; this moves it to the block's first line.</summary>
    private string MoveToFirstLine() => _lines.Count > 0 ? $"\r{Csi}{_lines.Count}A" : "\r";

    private string ToAnsi(string markup)
    {
        _ansi.GetStringBuilder().Clear();
        _renderer.Write(new Markup(markup));
        return _ansi.ToString();
    }

    private void Write(string text)
    {
        var output = _console.Profile.Out.Writer;
        output.Write(text);
        output.Flush();
    }
}
