using Spectre.Console.Rendering;

namespace Pubwiz.Tool;

/// <summary>
/// Shortcuts for writing widgets to the console.
/// </summary>
public static partial class Widgets
{
    public static void Write(IRenderable renderable, bool newLine = true)
    {
        AnsiConsole.Write(renderable);
        if (newLine)
        {
            AnsiConsole.WriteLine();
        }
    }

    public static void WriteMarkup(string markup, bool newLine = true) => Write(new Markup(markup), newLine);

    public static void WriteLines(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            AnsiConsole.WriteLine();
        }
    }

    /// <summary>
    /// Writes the header and version, shown at the top of every command.
    /// </summary>
    public static void WriteHeader()
    {
        Write(Header());
        WriteLines();
    }
}
