namespace Pubwiz.Tool.Prompts;

/// <summary>
/// Fits a list's columns into the terminal's width, so a row never wraps: a wrapped row takes two lines, which pushes
/// the end of the list off screen and breaks the live display.
/// </summary>
/// <remarks>
/// The columns on the right give way first, since the first ones say the most (an app's name, then its resource group):
/// each is cut to make room, or hidden when what's left of it would be too narrow to read. The first column (the name)
/// is cut only when it doesn't fit on its own.
/// </remarks>
public static class ListColumns
{
    /// <summary>The space between two columns.</summary>
    public const int Gap = 3;

    /// <summary>A column cut narrower than this is hidden instead (so "Running" becomes "Runn…", not "R…").</summary>
    public const int MinimumWidth = 5;

    /// <param name="widths">The widest text of each column.</param>
    /// <param name="available">The room for all the columns, gaps included.</param>
    /// <returns>Each column's width, where 0 means hidden. The first column is always at least 1 wide.</returns>
    public static int[] Fit(IReadOnlyList<int> widths, int available)
    {
        var fitted = widths.ToArray();
        for (var column = fitted.Length - 1; column > 0 && Overflow() > 0; column--)
        {
            var cut = fitted[column] - Overflow();
            fitted[column] = cut >= MinimumWidth ? cut : 0;
        }

        if (Overflow() > 0)
        {
            fitted[0] = Math.Max(fitted[0] - Overflow(), 1);
        }

        return fitted;

        int Overflow() => TotalWidth(fitted) - available;
    }

    /// <summary>The width of the visible columns and the gaps between them.</summary>
    public static int TotalWidth(IReadOnlyList<int> widths)
    {
        var visible = widths.Where(width => width > 0).ToList();
        return visible.Sum() + Gap * Math.Max(visible.Count - 1, 0);
    }
}
