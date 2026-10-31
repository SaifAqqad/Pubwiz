namespace Pubwiz.Tool.Prompts;

/// <summary>
/// Cursor, selection and scrolling of a <see cref="MultiSelectPrompt{T}"/>, without any console code.
/// </summary>
public sealed class MultiSelectState
{
    private readonly bool[] _selectable;
    private readonly HashSet<int> _selected = [];

    /// <param name="selectable">One entry per item: whether it can be selected (unselectable items can still be highlighted).</param>
    /// <param name="pageSize">How many items fit on screen at once.</param>
    public MultiSelectState(IReadOnlyList<bool> selectable, int pageSize)
    {
        if (selectable.Count == 0)
        {
            throw new ArgumentException("At least one item is required.", nameof(selectable));
        }

        _selectable = selectable.ToArray();
        PageSize = Math.Clamp(pageSize, 1, selectable.Count);
    }

    public int Count => _selectable.Length;

    public int PageSize { get; }

    /// <summary>The highlighted item.</summary>
    public int Cursor { get; private set; }

    /// <summary>The first visible item.</summary>
    public int Offset { get; private set; }

    /// <summary>Selected item indexes, in list order.</summary>
    public IReadOnlyList<int> Selected => _selected.Order().ToList();

    public bool IsSelected(int index) => _selected.Contains(index);

    public bool IsSelectable(int index) => _selectable[index];

    /// <summary>Moves the cursor, wrapping around at both ends.</summary>
    public void Move(int delta)
    {
        Cursor = ((Cursor + delta) % Count + Count) % Count;
        KeepCursorVisible();
    }

    public void MoveTo(int index)
    {
        Cursor = Math.Clamp(index, 0, Count - 1);
        KeepCursorVisible();
    }

    public void ToggleCursor()
    {
        if (!_selectable[Cursor])
        {
            return;
        }

        if (!_selected.Remove(Cursor))
        {
            _selected.Add(Cursor);
        }
    }

    /// <summary>Selects every selectable item, or clears the selection when they're all selected already.</summary>
    public void ToggleAll()
    {
        var selectable = Enumerable.Range(0, Count).Where(i => _selectable[i]).ToList();
        if (selectable.All(_selected.Contains))
        {
            _selected.Clear();
        }
        else
        {
            _selected.UnionWith(selectable);
        }
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
