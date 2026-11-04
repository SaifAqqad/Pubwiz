namespace Pubwiz.Tool.Prompts;

/// <summary>
/// The order numbers and cursor of an <see cref="OrderPrompt{T}"/>, without any console code.
/// </summary>
/// <remarks>
/// Each item has a number from 1 to the item count. The rows are sorted by number (then by the items' own order),
/// so items with the same number sit together as a step. The cursor stays on its item when the item's number
/// changes and it moves to another step.
/// </remarks>
public sealed class OrderState
{
    private readonly int[] _orders;

    /// <summary>The digits typed for the cursor's item since it was last moved, so 1 then 2 gives 12 in a big batch.</summary>
    private string _typed = string.Empty;

    /// <param name="orders">The starting number of each item (clamped to 1 to the item count).</param>
    /// <param name="pageSize">How many rows fit on screen at once.</param>
    public OrderState(IReadOnlyList<int> orders, int pageSize)
    {
        if (orders.Count == 0)
        {
            throw new ArgumentException("At least one item is required.", nameof(orders));
        }

        _orders = orders.Select(order => Math.Clamp(order, 1, orders.Count)).ToArray();
        PageSize = Math.Max(pageSize, 1);
        Rows = SortRows();
    }

    public int Count => _orders.Length;

    public int PageSize { get; }

    /// <summary>The number of each item, in item order.</summary>
    public IReadOnlyList<int> Orders => _orders;

    /// <summary>The item index of each row: sorted by number, then by item order.</summary>
    public IReadOnlyList<int> Rows { get; private set; }

    /// <summary>The highlighted item.</summary>
    public int CursorItem { get; private set; }

    /// <summary>The highlighted row.</summary>
    public int CursorRow => IndexOf(Rows, CursorItem);

    /// <summary>The first row on screen.</summary>
    public int Offset { get; private set; }

    /// <summary>How many steps the numbers make (items with the same number are one step).</summary>
    public int StepCount => _orders.Distinct().Count();

    /// <summary>The step of each item, numbered 1, 2, ... without gaps, in item order.</summary>
    public IReadOnlyList<int> Steps
    {
        get
        {
            var distinct = _orders.Distinct().Order().ToList();
            return _orders.Select(order => distinct.IndexOf(order) + 1).ToList();
        }
    }

    /// <summary>Moves the cursor a row up or down, wrapping around at both ends.</summary>
    public void Move(int delta)
    {
        _typed = string.Empty;
        CursorItem = Rows[((CursorRow + delta) % Count + Count) % Count];
        KeepCursorVisible();
    }

    /// <summary>Moves the cursor to a row, stopping at both ends.</summary>
    public void MoveTo(int row)
    {
        _typed = string.Empty;
        CursorItem = Rows[Math.Clamp(row, 0, Count - 1)];
        KeepCursorVisible();
    }

    /// <summary>
    /// Sets the cursor item's number from a typed digit. Digits typed one after another make a bigger number while it
    /// fits (1 then 2 is 12 with 12 or more items); otherwise the digit starts over. Numbers outside 1 to the item count are ignored.
    /// </summary>
    public void Type(int digit)
    {
        if (int.TryParse(_typed + digit, out var combined) && _typed.Length > 0 && combined >= 1 && combined <= Count)
        {
            _typed += digit;
            SetOrder(combined);
        }
        else if (digit >= 1 && digit <= Count)
        {
            _typed = digit.ToString();
            SetOrder(digit);
        }
    }

    /// <summary>Changes the cursor item's number by <paramref name="delta"/>, within 1 to the item count.</summary>
    public void Change(int delta)
    {
        _typed = string.Empty;
        SetOrder(Math.Clamp(_orders[CursorItem] + delta, 1, Count));
    }

    /// <summary>Gives every item number 1: everything at the same time.</summary>
    public void AllAtOnce()
    {
        _typed = string.Empty;
        Array.Fill(_orders, 1);
        Update();
    }

    /// <summary>Numbers the items 1, 2, 3, ... in the order they're shown: one after another.</summary>
    public void OneByOne()
    {
        _typed = string.Empty;
        for (var row = 0; row < Count; row++)
        {
            _orders[Rows[row]] = row + 1;
        }

        Update();
    }

    /// <summary>How many items have the same number as <paramref name="item"/> (its step's size).</summary>
    public int StepSize(int item) => _orders.Count(order => order == _orders[item]);

    private void SetOrder(int order)
    {
        _orders[CursorItem] = order;
        Update();
    }

    private void Update()
    {
        Rows = SortRows();
        KeepCursorVisible();
    }

    private int[] SortRows() => Enumerable.Range(0, Count).OrderBy(i => _orders[i]).ThenBy(i => i).ToArray();

    private void KeepCursorVisible()
    {
        var row = CursorRow;
        if (row < Offset)
        {
            Offset = row;
        }
        else if (row >= Offset + PageSize)
        {
            Offset = row - PageSize + 1;
        }

        Offset = Math.Clamp(Offset, 0, Math.Max(Count - PageSize, 0));
    }

    private static int IndexOf(IReadOnlyList<int> rows, int item)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i] == item)
            {
                return i;
            }
        }

        return -1;
    }
}
