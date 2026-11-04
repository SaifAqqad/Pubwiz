namespace Pubwiz.Tool.Prompts;

/// <summary>The part of a <see cref="MultiSelectState"/> list that has the cursor.</summary>
public enum MultiSelectSection
{
    /// <summary>The items that aren't selected (and the exit row).</summary>
    List,

    /// <summary>The selected items, shown above the list.</summary>
    Selected
}

/// <summary>
/// Cursor, selection, search and scrolling of a <see cref="MultiSelectPrompt{T}"/>, without any console code.
/// </summary>
/// <remarks>
/// Selected items move out of the list into a section above it. The list shows the other items that match the search,
/// followed by an optional "Exit" row. Each section has its own cursor and scrolling, and Tab moves between them.
/// The selected section ignores the search, so it always shows the whole selection.
/// </remarks>
public sealed class MultiSelectState
{
    /// <summary>Rows the selected section uses besides its items: a blank line and its heading.</summary>
    /// <remarks>While nothing is selected, an always-shown section uses one more row for its placeholder.</remarks>
    public const int SelectedChromeHeight = 2;

    private readonly string[] _searchTexts;
    private readonly bool[] _selectable;
    private readonly HashSet<int> _selected = [];
    private List<int> _matches;
    private List<int> _visible;
    private List<int> _selectedRows = [];

    /// <param name="searchTexts">One entry per item: the text the search looks in.</param>
    /// <param name="selectable">One entry per item: whether it can be selected (unselectable items can still be highlighted).</param>
    /// <param name="pageSize">How many rows fit on screen at once, for both sections together.</param>
    /// <param name="hasExitRow">Adds an "Exit" row after the items.</param>
    /// <param name="alwaysShowSelected">Keeps the selected section on screen while nothing is selected, so the list doesn't move.</param>
    public MultiSelectState(IReadOnlyList<string> searchTexts, IReadOnlyList<bool> selectable, int pageSize, bool hasExitRow = false,
        bool alwaysShowSelected = false)
    {
        if (searchTexts.Count == 0)
        {
            throw new ArgumentException("At least one item is required.", nameof(searchTexts));
        }

        if (searchTexts.Count != selectable.Count)
        {
            throw new ArgumentException("There must be one entry per item.", nameof(selectable));
        }

        _searchTexts = searchTexts.ToArray();
        _selectable = selectable.ToArray();
        _matches = Enumerable.Range(0, _searchTexts.Length).ToList();
        _visible = _matches.ToList();
        PageSize = Math.Max(pageSize, 1);
        HasExitRow = hasExitRow;
        AlwaysShowSelected = alwaysShowSelected;
    }

    public int PageSize { get; }

    public bool HasExitRow { get; }

    /// <summary>The selected section is on screen while nothing is selected (with a placeholder row).</summary>
    public bool AlwaysShowSelected { get; }

    /// <summary>Whether the selected section is on screen.</summary>
    public bool ShowsSelected => AlwaysShowSelected || _selectedRows.Count > 0;

    /// <summary>The search text; empty shows every item.</summary>
    public string Search { get; private set; } = string.Empty;

    /// <summary>The section that has the cursor.</summary>
    public MultiSelectSection Focus { get; private set; }

    /// <summary>The indexes of the list's items: the ones that match the search and aren't selected, in list order.</summary>
    public IReadOnlyList<int> VisibleItems => _visible;

    /// <summary>The number of rows in the list: the visible items plus the exit row.</summary>
    public int RowCount => _visible.Count + (HasExitRow ? 1 : 0);

    /// <summary>The list's highlighted row. It's kept while the cursor is in the selected section.</summary>
    public int Cursor { get; private set; }

    /// <summary>The list's first row on screen.</summary>
    public int Offset { get; private set; }

    /// <summary>How many list rows fit on screen, after the selected section.</summary>
    public int ListPageSize => Math.Max(PageSize - (ShowsSelected ? Math.Max(SelectedPageSize, 1) + SelectedChromeHeight : 0), 1);

    /// <summary>Selected item indexes, in list order, including items hidden by the search.</summary>
    public IReadOnlyList<int> Selected => _selectedRows;

    /// <summary>The highlighted row of the selected section.</summary>
    public int SelectedCursor { get; private set; }

    /// <summary>The selected section's first row on screen.</summary>
    public int SelectedOffset { get; private set; }

    /// <summary>How many selected items fit on screen: at most half the page, so the list stays usable.</summary>
    public int SelectedPageSize => _selectedRows.Count == 0 ? 0 : Math.Min(_selectedRows.Count, Math.Max(PageSize / 2, 1));

    /// <summary>
    /// The highlighted item of the focused section, or <c>null</c> when the exit row is highlighted or nothing matches the search.
    /// </summary>
    public int? CursorItem => Focus == MultiSelectSection.Selected ? _selectedRows[SelectedCursor] : ListCursorItem;

    public bool IsOnExitRow => Focus == MultiSelectSection.List && HasExitRow && Cursor == _visible.Count;

    public bool IsSelected(int item) => _selected.Contains(item);

    public bool IsSelectable(int item) => _selectable[item];

    private int? ListCursorItem => Cursor < _visible.Count ? _visible[Cursor] : null;

    /// <summary>Moves the cursor within its section, wrapping around at both ends.</summary>
    public void Move(int delta)
    {
        var count = Focus == MultiSelectSection.Selected ? _selectedRows.Count : RowCount;
        if (count == 0)
        {
            return;
        }

        MoveTo((((Focus == MultiSelectSection.Selected ? SelectedCursor : Cursor) + delta) % count + count) % count);
    }

    /// <summary>Moves the cursor to a row of its section, stopping at both ends.</summary>
    public void MoveTo(int row)
    {
        if (Focus == MultiSelectSection.Selected)
        {
            SelectedCursor = Math.Clamp(row, 0, _selectedRows.Count - 1);
        }
        else
        {
            Cursor = Math.Clamp(row, 0, Math.Max(RowCount - 1, 0));
        }

        KeepCursorsVisible();
    }

    /// <summary>Moves the cursor a page up (<paramref name="direction"/> -1) or down (1) within its section.</summary>
    public void MovePage(int direction) => MoveTo(Focus == MultiSelectSection.Selected
        ? SelectedCursor + direction * SelectedPageSize
        : Cursor + direction * ListPageSize);

    /// <summary>Moves the cursor to the last row of its section.</summary>
    public void MoveToEnd() => MoveTo(int.MaxValue);

    /// <summary>
    /// Moves the cursor between the list and the selected section. The list remembers its row,
    /// so coming back lands where it left. Does nothing while nothing is selected.
    /// </summary>
    public void SwitchSection()
    {
        Focus = Focus == MultiSelectSection.List && _selectedRows.Count > 0 ? MultiSelectSection.Selected : MultiSelectSection.List;
    }

    /// <summary>
    /// Selects the highlighted list item, which moves it to the selected section and leaves the cursor on the next item;
    /// or unselects the highlighted selected item, which puts it back in the list and leaves the cursor on the next selected item.
    /// </summary>
    public void ToggleCursor()
    {
        if (CursorItem is not { } item || !_selectable[item])
        {
            return;
        }

        if (Focus == MultiSelectSection.Selected)
        {
            _selected.Remove(item);
            UpdateRows(keepListItem: ListCursorItem);
        }
        else
        {
            _selected.Add(item);
            UpdateRows(keepListItem: null); // the row stays the same, which is now the next item
        }
    }

    /// <summary>
    /// Selects every matching selectable item, or unselects them when they're all selected already.
    /// With a search, that's every match.
    /// </summary>
    public void ToggleAll()
    {
        var selectable = _matches.Where(i => _selectable[i]).ToList();
        if (selectable.All(_selected.Contains))
        {
            _selected.ExceptWith(selectable);
        }
        else
        {
            _selected.UnionWith(selectable);
        }

        UpdateRows(keepListItem: ListCursorItem);
    }

    /// <summary>
    /// Changes the search (case-insensitive, anywhere in the item's text) and moves the cursor to the best match in the list.
    /// Items that start with the search come first, then items with a word that starts with it, then the rest;
    /// each group keeps the list order.
    /// </summary>
    public void SetSearch(string search)
    {
        Search = search;
        Focus = MultiSelectSection.List;
        _matches = Enumerable.Range(0, _searchTexts.Length)
            .Select(i => (Item: i, Rank: Rank(_searchTexts[i], search)))
            .Where(match => match.Rank >= 0)
            .OrderBy(match => match.Rank) // stable, so the list order stays within a group
            .Select(match => match.Item)
            .ToList();

        Cursor = 0;
        Offset = 0;
        UpdateRows(keepListItem: null);
    }

    /// <returns>0 for a prefix match, 1 for a word-start match, 2 for any other match, -1 for no match.</returns>
    internal static int Rank(string text, string search)
    {
        if (search.Length == 0)
        {
            return 0;
        }

        var index = text.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return -1;
        }

        if (index == 0)
        {
            return 0;
        }

        for (; index > 0; index = text.IndexOf(search, index + 1, StringComparison.OrdinalIgnoreCase))
        {
            if (!char.IsLetterOrDigit(text[index - 1]))
            {
                return 1;
            }
        }

        return 2;
    }

    /// <summary>
    /// Rebuilds both sections after the selection or the search changed. The list cursor stays on
    /// <paramref name="keepListItem"/> when it's still listed, otherwise on the same row (the next item).
    /// </summary>
    private void UpdateRows(int? keepListItem)
    {
        var wasOnExitRow = HasExitRow && Cursor == _visible.Count && _visible.Count > 0;
        _visible = _matches.Where(i => !_selected.Contains(i)).ToList();
        _selectedRows = _selected.Order().ToList();

        if (wasOnExitRow)
        {
            Cursor = _visible.Count;
        }
        else if (keepListItem is { } item && _visible.IndexOf(item) is >= 0 and var row)
        {
            Cursor = row;
        }
        else
        {
            // The exit row isn't "the next item": stay on the last item while there's one.
            Cursor = Math.Min(Cursor, Math.Max(_visible.Count - 1, 0));
        }

        if (_selectedRows.Count == 0)
        {
            Focus = MultiSelectSection.List;
            SelectedCursor = 0;
        }
        else
        {
            SelectedCursor = Math.Min(SelectedCursor, _selectedRows.Count - 1);

            // With every item selected, the list holds only the exit row; leaving the cursor there would make
            // Enter exit instead of confirming the selection.
            if (_visible.Count == 0)
            {
                Focus = MultiSelectSection.Selected;
            }
        }

        KeepCursorsVisible();
    }

    /// <summary>Scrolls both sections so their cursors are on screen, without leaving empty rows at the end.</summary>
    private void KeepCursorsVisible()
    {
        Offset = Scroll(Cursor, Offset, ListPageSize, RowCount);
        SelectedOffset = Scroll(SelectedCursor, SelectedOffset, SelectedPageSize, _selectedRows.Count);

        static int Scroll(int cursor, int offset, int pageSize, int count)
        {
            if (cursor < offset)
            {
                offset = cursor;
            }
            else if (cursor >= offset + pageSize)
            {
                offset = cursor - pageSize + 1;
            }

            return Math.Clamp(offset, 0, Math.Max(count - pageSize, 0));
        }
    }
}
