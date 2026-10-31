namespace Pubwiz.Tool.Rendering;

/// <summary>
/// Helpers that make text fit on screen.
/// </summary>
public static class DisplayText
{
    /// <summary>
    /// Cuts <paramref name="text"/> to <paramref name="maxLength"/> characters, ending with "…" when cut.
    /// Used for single-line output (log tails, live details) that must never wrap.
    /// </summary>
    public static string Truncate(string text, int maxLength)
    {
        if (maxLength <= 1)
        {
            return string.Empty;
        }

        return text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength - 1), "…");
    }

    /// <summary>
    /// A shorter form of a path: relative to the current folder when inside it, otherwise with the home folder as <c>~</c>.
    /// URLs and other non-paths are returned unchanged.
    /// </summary>
    public static string ShortPath(string pathOrUrl)
    {
        if (!Path.IsPathFullyQualified(pathOrUrl))
        {
            return pathOrUrl;
        }

        var currentFolder = Directory.GetCurrentDirectory();
        var relative = Path.GetRelativePath(currentFolder, pathOrUrl);
        if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathFullyQualified(relative))
        {
            return relative;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return pathOrUrl.StartsWith(home, StringComparison.Ordinal) ? "~" + pathOrUrl[home.Length..] : pathOrUrl;
    }
}
