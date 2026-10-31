namespace Pubwiz.Tool.Prompts;

/// <summary>
/// Cursor, selection, search and scrolling of a <see cref="MultiSelectPrompt{T}"/>, without any console code.
/// </summary>
/// <remarks>
/// The list shows the items that match the search, followed by an optional "Exit" row.
/// The cursor and scrolling work on those rows; the selection keeps items that the search hides.
/// </remarks>
public sealed class MultiSelectState
{
    private readonly string[] _searchTexts;
    private readonly bool[] _selectable;
    private readonly HashSet<int> _selected = [];
    private List<int> _visible;

    /// <param name="searchTexts">One entry per item: the text the search looks in.</param>
    /// <param name="selectable">One entry per item: whether it can be selected (unselectable items can still be highlighted).</param>
    /// <param name="pageSize">How many rows fit on screen at once.</param>
    /// <param name="hasExitRow">Adds an "Exit" row after the items.</param>
    public MultiSelectState(IReadOnlyList<string> searchTexts, IReadOnlyList<bool> selectable, int pageSize, bool hasExitRow = false)
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
        _visible = Enumerable.Range(0, _searchTexts.Length).ToList();
        PageSize = Math.Max(pageSize, 1);
        HasExitRow = hasExitRow;
    }

    public int PageSize { get; }

    public bool HasExitRow { get; }

    /// <summary>The search text; empty shows every item.</summary>
    public string Search { get; private set; } = string.Empty;

    /// <summary>The indexes of the items that match the search, in list order.</summary>
    public IReadOnlyList<int> VisibleItems => _visible;

    /// <summary>The number of rows: the visible items plus the exit row.</summary>
    public int RowCount => _visible.Count + (HasExitRow ? 1 : 0);

    /// <summary>The highlighted row.</summary>
    public int Cursor { get; private set; }

    /// <summary>The first row on screen.</summary>
    public int Offset { get; private set; }

    /// <summary>The highlighted item, or <c>null</c> when the exit row is highlighted or nothing matches the search.</summary>
    public int? CursorItem => Cursor < _visible.Count ? _visible[Cursor] : null;

    public bool IsOnExitRow => HasExitRow && Cursor == _visible.Count;

    /// <summary>Selected item indexes, in list order, including items hidden by the search.</summary>
    public IReadOnlyList<int> Selected => _selected.Order().ToList();

    public bool IsSelected(int item) => _selected.Contains(item);

    public bool IsSelectable(int item) => _selectable[item];

    /// <summary>Moves the cursor, wrapping around at both ends.</summary>
    public void Move(int delta)
    {
        if (RowCount == 0)
        {
            return;
        }

        Cursor = ((Cursor + delta) % RowCount + RowCount) % RowCount;
        KeepCursorVisible();
    }

    public void MoveTo(int row)
    {
        Cursor = Math.Clamp(row, 0, Math.Max(RowCount - 1, 0));
        KeepCursorVisible();
    }

    public void ToggleCursor()
    {
        if (CursorItem is not { } item || !_selectable[item])
        {
            return;
        }

        if (!_selected.Remove(item))
        {
            _selected.Add(item);
        }
    }

    /// <summary>
    /// Selects every visible selectable item, or unselects them when they're all selected already.
    /// With a search, that's every match.
    /// </summary>
    public void ToggleAll()
    {
        var selectable = _visible.Where(i => _selectable[i]).ToList();
        if (selectable.All(_selected.Contains))
        {
            _selected.ExceptWith(selectable);
        }
        else
        {
            _selected.UnionWith(selectable);
        }
    }

    /// <summary>Changes the search (case-insensitive, anywhere in the item's text) and moves the cursor to the first match.</summary>
    public void SetSearch(string search)
    {
        Search = search;
        _visible = Enumerable.Range(0, _searchTexts.Length)
            .Where(i => _searchTexts[i].Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Offset = 0;
        MoveTo(0);
    }

    private void KeepCursorVisible()
    {
        if (Cursor < Offset)
        {
            Offset = Cursor;
        }
        else if (Cursor >= Offset + PageSize)
        {
            Offset = Cursor - PageSize + 1;
        }
    }
}
