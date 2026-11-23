namespace Pubwiz.Tool.Prompts;

/// <summary>
/// The line of key hints under a list, with the keys in bold: <c>↑/↓ move · enter confirm · esc back</c>.
/// </summary>
public static class KeyHints
{
    /// <summary>
    /// The hints as markup, on one line: when they don't fit, the <paramref name="optional"/> ones are left out, in that
    /// order, until they do. A wrapped line would push the list off screen.
    /// </summary>
    /// <param name="keys">Each key and what it does, in display order.</param>
    /// <param name="width">The terminal's width.</param>
    /// <param name="optional">The keys that can be left out, least needed first, e.g. <c>ctrl+a</c>.</param>
    public static string ToMarkup(IEnumerable<(string Key, string Action)> keys, int width, params string[] optional)
    {
        var shown = keys.ToList();
        foreach (var key in optional)
        {
            if (PlainLength(shown) <= width)
            {
                break;
            }

            shown.RemoveAll(k => k.Key == key);
        }

        return string.Join(" [dim]·[/] ", shown.Select(k => $"[bold]{k.Key.EscapeMarkup()}[/] [dim]{k.Action.EscapeMarkup()}[/]"));
    }

    /// <summary>"key action" for each, with " · " between them.</summary>
    private static int PlainLength(List<(string Key, string Action)> keys) =>
        keys.Sum(k => k.Key.Length + 1 + k.Action.Length) + 3 * Math.Max(keys.Count - 1, 0);
}
