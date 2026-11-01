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

    /// <summary>
    /// Markup for a path or URL that terminals with hyperlink support (OSC 8) open on click: the text is
    /// <see cref="ShortPath"/> (or <paramref name="text"/>), the link is the full <c>file://</c> or web URL.
    /// Terminals without hyperlinks show the text only.
    /// </summary>
    /// <param name="pathOrUrl">A full path, or a URL. Anything else is returned as plain (escaped) text.</param>
    /// <param name="text">The text to show instead of the short path; plain text, it's escaped.</param>
    public static string Link(string pathOrUrl, string? text = null)
    {
        var shown = (text ?? ShortPath(pathOrUrl)).EscapeMarkup();
        return ToLinkUrl(pathOrUrl) is { } url ? $"[link={url}]{shown}[/]" : shown;
    }

    /// <summary>The URL a <see cref="Link"/> points to, or <c>null</c> when <paramref name="pathOrUrl"/> is neither a full path nor a web URL.</summary>
    internal static string? ToLinkUrl(string pathOrUrl)
    {
        string url;
        if (Path.IsPathFullyQualified(pathOrUrl))
        {
            url = new Uri(pathOrUrl).AbsoluteUri;
        }
        else if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            url = uri.AbsoluteUri;
        }
        else
        {
            return null;
        }

        // Inside a markup tag, brackets and spaces would end the tag early.
        return url.Replace("[", "%5B", StringComparison.Ordinal)
            .Replace("]", "%5D", StringComparison.Ordinal)
            .Replace(" ", "%20", StringComparison.Ordinal);
    }

    /// <summary>How long ago <paramref name="time"/> was, e.g. "just now", "5 minutes ago", "yesterday", "3 weeks ago".</summary>
    public static string RelativeTime(DateTimeOffset time, DateTimeOffset now)
    {
        var age = now - time;
        return age switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalHours: < 1 } => Plural((int)age.TotalMinutes, "minute"),
            { TotalDays: < 1 } => Plural((int)age.TotalHours, "hour"),
            { TotalDays: < 2 } => "yesterday",
            { TotalDays: < 14 } => Plural((int)age.TotalDays, "day"),
            { TotalDays: < 60 } => Plural((int)(age.TotalDays / 7), "week"),
            { TotalDays: < 365 } => Plural((int)(age.TotalDays / 30), "month"),
            _ => Plural((int)(age.TotalDays / 365), "year")
        };

        static string Plural(int count, string unit) => count == 1 ? $"1 {unit} ago" : $"{count} {unit}s ago";
    }
}
