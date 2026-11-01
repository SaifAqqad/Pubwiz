using Spectre.Console.Rendering;

namespace Pubwiz.Tool.Prompts;

/// <summary>
/// The result of a <see cref="MultiSelectPrompt{T}"/>.
/// </summary>
/// <param name="Selected">The items toggled with Space, in list order. Empty when Enter was pressed without selecting.</param>
/// <param name="Highlighted">The item under the cursor when Enter was pressed.</param>
public sealed record MultiSelectResult<T>(IReadOnlyList<T> Selected, T Highlighted);

/// <summary>
/// A searchable multi-select list that also reports the highlighted item.
/// </summary>
/// <remarks>
/// Spectre's <see cref="MultiSelectionPrompt{T}"/> can't say which item was highlighted when Enter is pressed
/// with nothing selected, and can't search. Pubwiz uses that case to mean "just this one", so it needs its own prompt.
/// Keys: ↑/↓ move, PgUp/PgDn/Home/End jump, Space toggles, Ctrl+A toggles all (or all matches), Enter confirms,
/// typing searches, Backspace edits the search, Esc clears the search, then exits or cancels.
/// </remarks>
public sealed class MultiSelectPrompt<T>(string title, IReadOnlyList<T> items, Func<T, IReadOnlyList<string>> columns) : IPrompt<MultiSelectResult<T>?>
    where T : notnull
{
    /// <summary>Rows used by the title, search, footer and hint, subtracted from the terminal height to get the page size.</summary>
    private const int ChromeHeight = 8;

    /// <summary>Lines below the list explaining extra behavior.</summary>
    public string? Hint { get; init; }

    /// <summary>
    /// A line of markup above the title that stays visible while choosing (e.g. the git branch), followed by a blank line.
    /// The page size counts it as one line, so it should fit the terminal's width.
    /// </summary>
    public string? Header { get; init; }

    /// <summary>Items that can't be selected are shown dimmed. All items are selectable by default.</summary>
    public Func<T, bool> IsSelectable { get; init; } = _ => true;

    /// <summary>
    /// Adds a row with this label after the items. Choosing it (or pressing Esc, see <see cref="EscapeChoosesExitRow"/>)
    /// returns <c>null</c>. Without it, Esc throws <see cref="PromptCancelledException"/>.
    /// </summary>
    public string? ExitLabel { get; init; }

    /// <summary>The icon of the exit row (✕ by default; plain-text fallback for terminals without Unicode).</summary>
    public (string Unicode, string Ascii) ExitIcon { get; init; } = ("✕", "x");

    /// <summary>
    /// <c>true</c> (the default): Esc picks the exit row. <c>false</c>: Esc throws <see cref="PromptCancelledException"/>
    /// even with an exit row, for rows that aren't a way out (e.g. "Refresh").
    /// </summary>
    public bool EscapeChoosesExitRow { get; init; } = true;

    /// <summary>
    /// Pick one item: no checkboxes, Space and Ctrl+A don't select, and Space can be part of the search.
    /// The answer is <see cref="MultiSelectResult{T}.Highlighted"/>.
    /// </summary>
    public bool SingleSelect { get; init; }

    /// <summary>
    /// A newer <see cref="Header"/>, drawn as soon as it's ready while the list stays open (e.g. the git line once a
    /// background fetch is done). <c>null</c> or a failed task keeps the current header.
    /// </summary>
    public Task<string?>? HeaderUpdate { get; init; }

    /// <summary>The header being shown: <see cref="Header"/>, then <see cref="HeaderUpdate"/>'s result.</summary>
    private string? _header;

    public MultiSelectResult<T>? Show(IAnsiConsole console) => ShowAsync(console, CancellationToken.None).GetAwaiter().GetResult();

    /// <returns>The chosen items, or <c>null</c> when the exit row was chosen.</returns>
    public async Task<MultiSelectResult<T>?> ShowAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        var searchTexts = items.Select(item => string.Join(' ', columns(item))).ToList();
        var headerHeight = Header is null && HeaderUpdate is null ? 0 : 2;
        var state = new MultiSelectState(searchTexts, items.Select(IsSelectable).ToList(), console.Profile.Height - ChromeHeight - headerHeight, hasExitRow: ExitLabel is not null);
        _header = Header;

        console.Cursor.Hide();
        try
        {
            return await console.Live(Render(state, console)).AutoClear(true).StartAsync(async context =>
            {
                // A live display draws nothing until it's refreshed, so show the list before waiting for a key.
                context.Refresh();

                var headerUpdate = HeaderUpdate;
                Task<ConsoleKeyInfo?>? keyRead = null;
                while (true)
                {
                    // The same key read is kept while waiting for the header, so no key press is lost.
                    keyRead ??= console.Input.ReadKeyAsync(intercept: true, cancellationToken);
                    if (headerUpdate is not null && (headerUpdate.IsCompleted || await Task.WhenAny(keyRead, headerUpdate) == headerUpdate))
                    {
                        if (headerUpdate.IsCompletedSuccessfully && headerUpdate.Result is { } newHeader)
                        {
                            _header = newHeader;
                            context.UpdateTarget(Render(state, console));
                        }

                        headerUpdate = null;
                        continue;
                    }

                    var key = await keyRead;
                    keyRead = null;
                    if (key is not null && HandleKey(key.Value, state, out var result))
                    {
                        return result;
                    }

                    context.UpdateTarget(Render(state, console));
                }
            });
        }
        finally
        {
            console.Cursor.Show();
        }
    }

    /// <returns><c>true</c> when the prompt is done; <paramref name="result"/> is then the answer (<c>null</c> for the exit row).</returns>
    private bool HandleKey(ConsoleKeyInfo key, MultiSelectState state, out MultiSelectResult<T>? result)
    {
        result = null;
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                state.Move(-1);
                break;
            case ConsoleKey.DownArrow:
                state.Move(1);
                break;
            case ConsoleKey.PageUp:
                state.MoveTo(state.Cursor - state.PageSize);
                break;
            case ConsoleKey.PageDown:
                state.MoveTo(state.Cursor + state.PageSize);
                break;
            case ConsoleKey.Home:
                state.MoveTo(0);
                break;
            case ConsoleKey.End:
                state.MoveTo(state.RowCount - 1);
                break;
            case ConsoleKey.Spacebar when SingleSelect:
                state.SetSearch(state.Search + ' ');
                break;
            case ConsoleKey.Spacebar:
                state.ToggleCursor();
                break;
            case ConsoleKey.A when key.Modifiers.HasFlag(ConsoleModifiers.Control):
                if (!SingleSelect)
                {
                    state.ToggleAll();
                }

                break;
            case ConsoleKey.Backspace when state.Search.Length > 0:
                state.SetSearch(state.Search[..^1]);
                break;
            case ConsoleKey.Enter:
                return TryConfirm(state, out result);
            case ConsoleKey.Escape when state.Search.Length > 0:
                state.SetSearch(string.Empty);
                break;
            case ConsoleKey.Escape when state.HasExitRow && EscapeChoosesExitRow:
                return true;
            case ConsoleKey.Escape:
                throw new PromptCancelledException();
            default:
                if (IsSearchCharacter(key))
                {
                    state.SetSearch(state.Search + key.KeyChar);
                }

                break;
        }

        return false;
    }

    /// <summary>
    /// Enter returns the selection, or just the highlighted item when nothing is selected.
    /// It does nothing on an item that can't be selected, or when nothing matches the search and nothing is selected.
    /// </summary>
    private bool TryConfirm(MultiSelectState state, out MultiSelectResult<T>? result)
    {
        result = null;
        if (state.IsOnExitRow)
        {
            return true;
        }

        var selected = state.Selected.Select(i => items[i]).ToList();
        if (selected.Count > 0)
        {
            result = new MultiSelectResult<T>(selected, state.CursorItem is { } cursor ? items[cursor] : selected[0]);
            return true;
        }

        if (state.CursorItem is { } item && state.IsSelectable(item))
        {
            result = new MultiSelectResult<T>([], items[item]);
            return true;
        }

        return false;
    }

    /// <summary>Printable characters search, except Space (which selects) and shortcuts such as Ctrl+A.</summary>
    private static bool IsSearchCharacter(ConsoleKeyInfo key) =>
        !char.IsControl(key.KeyChar)
        && key.KeyChar != ' '
        && (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;

    private IRenderable Render(MultiSelectState state, IAnsiConsole console)
    {
        var unicode = console.Profile.Capabilities.Unicode;
        var columnCount = columns(items[0]).Count;

        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(1));
        for (var i = 0; i < columnCount; i++)
        {
            grid.AddColumn(new GridColumn().NoWrap().PadRight(3));
        }

        var lastRow = Math.Min(state.Offset + state.PageSize, state.RowCount);
        for (var row = state.Offset; row < lastRow; row++)
        {
            grid.AddRow(row < state.VisibleItems.Count
                ? RenderItem(state.VisibleItems[row], state, isCursor: row == state.Cursor, unicode)
                : RenderExitRow(columnCount, isCursor: row == state.Cursor, unicode));
        }

        var rows = new List<IRenderable>();
        if (_header is not null)
        {
            rows.Add(new Markup(_header));
            rows.Add(new Text(string.Empty));
        }

        rows.AddRange([new Markup(title), RenderSearch(state)]);
        rows.Add(state.Offset > 0 ? new Markup("[dim]  ↑ more[/]") : new Text(string.Empty));
        if (state.VisibleItems.Count == 0)
        {
            rows.Add(new Markup($"[dim]  Nothing matches '{state.Search.EscapeMarkup()}'[/]"));
        }

        rows.Add(grid);
        rows.Add(lastRow < state.RowCount ? new Markup("[dim]  ↓ more[/]") : new Text(string.Empty));
        rows.Add(RenderKeys(state));
        if (Hint is not null)
        {
            rows.Add(new Markup($"[dim]{Hint}[/]"));
        }

        return new Rows(rows);
    }

    private static Markup RenderSearch(MultiSelectState state) => state.Search.Length == 0
        ? new Markup("[dim]  Type to search[/]")
        : new Markup($"  [bold]Search:[/] [{Widgets.PrimaryColorMarkup}]{state.Search.EscapeMarkup()}[/]");

    private Markup[] RenderItem(int index, MultiSelectState state, bool isCursor, bool unicode)
    {
        var selectable = state.IsSelectable(index);
        var style = !selectable ? "grey"
            : isCursor ? $"{Widgets.PrimaryColorMarkup} bold"
            : "default";

        var pointer = isCursor ? $"[{Widgets.PrimaryColorMarkup}]{(unicode ? "❯" : ">")}[/]" : " ";
        var cells = columns(items[index]).Select((text, column) =>
        {
            // Only the first column (the name) is highlighted; the rest stay dim to keep the list calm.
            var cellStyle = column == 0 || !selectable ? style : "dim";
            return new Markup($"[{cellStyle}]{HighlightSearch(text, state.Search)}[/]");
        });

        if (SingleSelect)
        {
            return [new Markup(pointer), .. cells];
        }

        var checkbox = state.IsSelected(index)
            ? $"[{Widgets.PrimaryColorMarkup}]{(unicode ? "◉" : "[[x]]")}[/]"
            : $"[{style}]{(unicode ? "○" : "[[ ]]")}[/]";

        return [new Markup($"{pointer} {checkbox}"), .. cells];
    }

    private Markup[] RenderExitRow(int columnCount, bool isCursor, bool unicode)
    {
        var style = isCursor ? $"{Widgets.PrimaryColorMarkup} bold" : "dim";
        var pointer = isCursor ? (unicode ? "❯" : ">") : " ";
        var icon = (unicode ? ExitIcon.Unicode : ExitIcon.Ascii).EscapeMarkup();

        var label = new Markup($"[{style}]{ExitLabel!.EscapeMarkup()}[/]");
        var empty = Enumerable.Range(0, columnCount - 1).Select(_ => new Markup(string.Empty));
        return SingleSelect
            ? [new Markup($"[{style}]{pointer}[/]"), new Markup($"[{style}]{icon} {ExitLabel.EscapeMarkup()}[/]"), .. empty]
            : [new Markup($"[{style}]{pointer} {icon}[/]"), label, .. empty];
    }

    /// <summary>The key hints, with the keys in bold: <c>↑/↓ move · space select · ...</c></summary>
    private Markup RenderKeys(MultiSelectState state)
    {
        var escape = state.Search.Length > 0 ? "clear search" : state.HasExitRow && EscapeChoosesExitRow ? "exit" : SingleSelect ? "back" : "cancel";
        (string Key, string Action)[] keys = SingleSelect
            ? [("↑/↓", "move"), ("enter", "choose"), ("esc", escape)]
            :
        [
            ("↑/↓", "move"),
            ("space", "select"),
            ("ctrl+a", state.Search.Length > 0 ? "select matches" : "select all"),
            ("enter", "confirm"),
            ("esc", escape)
        ];

        return new Markup(string.Join(" [dim]·[/] ", keys.Select(k => $"[bold]{k.Key}[/] [dim]{k.Action}[/]")));
    }

    /// <summary>Underlines the first match of the search in <paramref name="text"/> and escapes the rest.</summary>
    private static string HighlightSearch(string text, string search)
    {
        var start = search.Length == 0 ? -1 : text.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return text.EscapeMarkup();
        }

        var before = text[..start].EscapeMarkup();
        var match = text.Substring(start, search.Length).EscapeMarkup();
        var after = text[(start + search.Length)..].EscapeMarkup();
        return $"{before}[underline]{match}[/]{after}";
    }
}
