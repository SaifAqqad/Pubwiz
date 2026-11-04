using System.Text;

namespace Pubwiz.Tool.Rendering;

/// <summary>
/// A block of lines drawn at the cursor and redrawn in place, rewriting only what changed.
/// </summary>
/// <remarks>
/// Spectre.Console's live display rewrites every line, padded to the terminal's width, on each refresh. With a batch
/// of projects that's several KB ten times a second, which makes the terminal lag while the builds keep the machine
/// busy. Here only the lines that changed are written, and within a line only the parts that changed: a spinner tick
/// moves the cursor to the spinner and draws that one character. Each frame is also wrapped in synchronized-output
/// markers (DEC mode 2026), so terminals that support them show it at once instead of line by line; others ignore them.
/// <para>
/// The lines are markup, and each must fit on one terminal line (no wrapping), with the block shorter than the
/// terminal, so the cursor can always move back to its first line. <see cref="Spinner"/> in the markup is drawn as the
/// current spinner frame, so a line whose only change is its spinner reuses the escape codes made for it before.
/// </para>
/// </remarks>
internal sealed class LiveRegion : IDisposable
{
    /// <summary>Stands for the spinner in the markup; <see cref="Update"/> draws the frame it's given in its place.</summary>
    public const char Spinner = '\uE000';

    private const string Csi = "\u001b[";
    private const string BeginFrame = Csi + "?2026h";
    private const string EndFrame = Csi + "?2026l";
    private const string ClearLine = Csi + "2K";
    private const string ClearToEnd = Csi + "K";
    private const string ClearBelow = Csi + "J";

    /// <summary>How many lines' escape codes are kept; build output makes most log lines new, so it's emptied when full.</summary>
    private const int AnsiCacheSize = 512;

    private readonly IAnsiConsole _console;
    private readonly StringWriter _ansi = new();
    private readonly IAnsiConsole _renderer;

    /// <summary>
    /// Escape codes of lines drawn recently, by markup. Lines often come back (a job's header between two seconds, a
    /// step that's done, a spinner's line), and turning markup into escape codes is the costly part of a redraw.
    /// </summary>
    private readonly Dictionary<string, string> _ansiCache = new(StringComparer.Ordinal);

    /// <summary>The escape codes of the lines on screen, and their runs (<c>null</c> when they can't be split), to tell what changed.</summary>
    private List<(string Ansi, Run[]? Runs)> _lines = [];

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
    /// <param name="lines">The lines, as markup.</param>
    /// <param name="spinner">What to draw where a line has <see cref="Spinner"/>, e.g. the current spinner frame.</param>
    public void Update(IReadOnlyList<string> lines, string spinner = " ")
    {
        // A resized terminal may have rewrapped the old lines, so the whole block is drawn again.
        var width = _console.Profile.Width;
        var redrawAll = width != _width;
        if (redrawAll)
        {
            _ansiCache.Clear(); // lines are cut to the width, so the old ones no longer fit
        }

        _width = width;
        _renderer.Profile.Width = width;

        var frame = new StringBuilder(BeginFrame);
        frame.Append(MoveToFirstLine());
        if (redrawAll)
        {
            frame.Append(ClearBelow);
        }

        var drawn = new List<(string, Run[]?)>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var ansi = ToAnsi(lines[i]).Replace(Spinner.ToString(), spinner, StringComparison.Ordinal);
            var runs = Run.Split(ansi);
            if (redrawAll || i >= _lines.Count)
            {
                frame.Append(ClearLine).Append(ansi);
            }
            else if (ansi != _lines[i].Ansi)
            {
                WriteChanges(frame, _lines[i].Runs, runs, ansi);
            }

            frame.Append("\r\n");
            drawn.Add((ansi, runs));
        }

        // The new frame is shorter: clear what's left of the old one.
        if (lines.Count < _lines.Count)
        {
            frame.Append(ClearBelow);
        }

        frame.Append(EndFrame);
        Write(frame.ToString());
        _lines = drawn;
    }

    /// <summary>Removes the block and shows the cursor again, leaving the cursor where the block started.</summary>
    public void Dispose()
    {
        Write(MoveToFirstLine() + ClearBelow + Csi + "?25h");
        _lines = [];
    }

    /// <summary>
    /// Writes what differs between a line on screen and its new version, with the cursor at the line's start:
    /// the runs that changed when they keep their width (e.g. a spinner), otherwise everything from the first change on.
    /// </summary>
    /// <param name="frame">The frame being built.</param>
    /// <param name="old">The runs of the line on screen, or <c>null</c> when it couldn't be split.</param>
    /// <param name="runs">The runs of the new line, or <c>null</c> when it can't be split.</param>
    /// <param name="ansi">The new line's escape codes.</param>
    private static void WriteChanges(StringBuilder frame, Run[]? old, Run[]? runs, string ansi)
    {
        if (old is null || runs is null || old.Length != runs.Length)
        {
            frame.Append(ClearLine).Append(ansi);
            return;
        }

        var first = -1;
        var sameWidths = true;
        for (var r = 0; r < runs.Length; r++)
        {
            if (runs[r].Ansi == old[r].Ansi)
            {
                continue;
            }

            if (first < 0)
            {
                first = r;
            }

            sameWidths &= runs[r].Width == old[r].Width;
        }

        if (first < 0)
        {
            return; // unchanged
        }

        if (sameWidths)
        {
            // Only some runs changed, in place: draw just those (a spinner tick is one character).
            for (var r = first; r < runs.Length; r++)
            {
                if (runs[r].Ansi != old[r].Ansi)
                {
                    frame.Append(MoveToColumn(runs[r].Column)).Append(runs[r].Ansi);
                }
            }

            return;
        }

        // A run changed its width, so everything after it moved: rewrite from there and clear the rest of the line.
        frame.Append(MoveToColumn(runs[first].Column));
        for (var r = first; r < runs.Length; r++)
        {
            frame.Append(runs[r].Ansi);
        }

        frame.Append(ClearToEnd);
    }

    /// <summary>Moves the cursor to a column of the current line (0 is the first).</summary>
    private static string MoveToColumn(int column) => $"{Csi}{column + 1}G";

    /// <summary>The cursor sits at the start of the line below the block; this moves it to the block's first line.</summary>
    private string MoveToFirstLine() => _lines.Count > 0 ? $"\r{Csi}{_lines.Count}A" : "\r";

    private string ToAnsi(string markup)
    {
        if (_ansiCache.TryGetValue(markup, out var cached))
        {
            return cached;
        }

        _ansi.GetStringBuilder().Clear();
        _renderer.Write(new Markup(markup));
        var ansi = _ansi.ToString();

        if (_ansiCache.Count >= AnsiCacheSize)
        {
            _ansiCache.Clear();
        }

        _ansiCache[markup] = ansi;
        return ansi;
    }

    private void Write(string text)
    {
        var output = _console.Profile.Out.Writer;
        output.Write(text);
        output.Flush();
    }

    /// <summary>
    /// A piece of a rendered line that starts and ends with the default style: a styled piece (from its style codes to
    /// its reset) or plain text. A run can be drawn on its own after moving the cursor to <see cref="Column"/>.
    /// </summary>
    /// <param name="Ansi">Its escape codes and text.</param>
    /// <param name="Column">The column it starts at.</param>
    /// <param name="Width">How many columns it takes.</param>
    internal readonly record struct Run(string Ansi, int Column, int Width)
    {
        /// <summary>
        /// Splits a rendered line into runs, or returns <c>null</c> when it has something whose width isn't certain
        /// (wide or combining characters, emoji, escape codes other than colours), so the caller rewrites it whole.
        /// </summary>
        public static Run[]? Split(string ansi)
        {
            var runs = new List<Run>();
            var current = new StringBuilder();
            int column = 0, start = 0, width = 0;
            var styled = false;

            for (var i = 0; i < ansi.Length;)
            {
                if (ansi[i] == '\u001b')
                {
                    // Only colour and style codes (ESC [ ... m) are expected; anything else isn't split.
                    if (i + 1 >= ansi.Length || ansi[i + 1] != '[')
                    {
                        return null;
                    }

                    var end = i + 2;
                    while (end < ansi.Length && ansi[end] is >= '0' and <= '9' or ';')
                    {
                        end++;
                    }

                    if (end >= ansi.Length || ansi[end] != 'm')
                    {
                        return null;
                    }

                    var code = ansi[i..(end + 1)];
                    var isReset = end == i + 2 || code == Csi + "0m";
                    if (!isReset && !styled && current.Length > 0)
                    {
                        Close(); // plain text before a style starts
                    }

                    current.Append(code);
                    styled = !isReset;
                    if (isReset)
                    {
                        Close();
                    }

                    i = end + 1;
                    continue;
                }

                var c = ansi[i];
                if (c is '\r' or '\n')
                {
                    i++;
                    continue;
                }

                if (!IsNarrow(c))
                {
                    return null;
                }

                current.Append(c);
                width++;
                i++;
            }

            Close();
            return [.. runs];

            void Close()
            {
                if (current.Length == 0)
                {
                    return;
                }

                runs.Add(new Run(current.ToString(), start, width));
                column = start + width;
                start = column;
                width = 0;
                current.Clear();
            }
        }

        /// <summary>
        /// Characters that surely take one column, left to right: Latin, Greek and Cyrillic without combining marks,
        /// and the punctuation, arrows, box drawing, shapes and braille (spinners) used here. Anything else (CJK and
        /// emoji, right-to-left scripts, combining marks) makes the line be rewritten whole instead.
        /// </summary>
        private static bool IsNarrow(char c) =>
            c is >= ' ' and < '\u007F'
            || c is >= '\u00A0' and < '\u0590' and not (>= '\u0300' and <= '\u036F')
            || c is >= '\u2010' and <= '\u2BFF' and not ('\u2329' or '\u232A');
    }
}
