using Spectre.Console.Rendering;

namespace Pubwiz.Tool.Prompts;

/// <summary>
/// The result of a <see cref="MultiSelectPrompt{T}"/>.
/// </summary>
/// <param name="Selected">The items toggled with Space, in list order. Empty when Enter was pressed without selecting.</param>
/// <param name="Highlighted">The item under the cursor when Enter was pressed.</param>
public sealed record MultiSelectResult<T>(IReadOnlyList<T> Selected, T Highlighted);

/// <summary>
/// A multi-select list that also reports the highlighted item.
/// </summary>
/// <remarks>
/// Spectre's <see cref="MultiSelectionPrompt{T}"/> can't say which item was highlighted when Enter is pressed
/// with nothing selected. Pubwiz uses that case to mean "just this one", so it needs its own prompt.
/// Keys: ↑/↓ (or k/j) move, PgUp/PgDn/Home/End jump, Space toggles, A toggles all, Enter confirms, Esc cancels.
/// </remarks>
public sealed class MultiSelectPrompt<T>(string title, IReadOnlyList<T> items, Func<T, IReadOnlyList<string>> columns) : IPrompt<MultiSelectResult<T>>
    where T : notnull
{
    /// <summary>Rows used by the title and the footer, subtracted from the terminal height to get the page size.</summary>
    private const int ChromeHeight = 7;

    /// <summary>Lines below the list explaining extra behavior.</summary>
    public string? Hint { get; init; }

    /// <summary>Items that can't be selected are shown dimmed. All items are selectable by default.</summary>
    public Func<T, bool> IsSelectable { get; init; } = _ => true;

    public MultiSelectResult<T> Show(IAnsiConsole console) => ShowAsync(console, CancellationToken.None).GetAwaiter().GetResult();

    public async Task<MultiSelectResult<T>> ShowAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        var state = new MultiSelectState(items.Select(IsSelectable).ToList(), console.Profile.Height - ChromeHeight);

        console.Cursor.Hide();
        try
        {
            return await console.Live(Render(state, console)).AutoClear(true).StartAsync(async context =>
            {
                while (true)
                {
                    var key = await console.Input.ReadKeyAsync(intercept: true, cancellationToken);
                    if (key is not null && HandleKey(key.Value, state) is { } result)
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

    /// <returns>The result when the prompt is done, otherwise <c>null</c>.</returns>
    private MultiSelectResult<T>? HandleKey(ConsoleKeyInfo key, MultiSelectState state)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow or ConsoleKey.K:
                state.Move(-1);
                break;
            case ConsoleKey.DownArrow or ConsoleKey.J:
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
                state.MoveTo(state.Count - 1);
                break;
            case ConsoleKey.Spacebar:
                state.ToggleCursor();
                break;
            case ConsoleKey.A:
                state.ToggleAll();
                break;
            case ConsoleKey.Enter:
                return new MultiSelectResult<T>(state.Selected.Select(i => items[i]).ToList(), items[state.Cursor]);
            case ConsoleKey.Escape:
                throw new OperationCanceledException();
        }

        return null;
    }

    private IRenderable Render(MultiSelectState state, IAnsiConsole console)
    {
        var unicode = console.Profile.Capabilities.Unicode;
        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(1));
        var columnCount = items.Count > 0 ? columns(items[0]).Count : 0;
        for (var i = 0; i < columnCount; i++)
        {
            grid.AddColumn(new GridColumn().NoWrap().PadRight(3));
        }

        var lastVisible = Math.Min(state.Offset + state.PageSize, state.Count);
        for (var index = state.Offset; index < lastVisible; index++)
        {
            grid.AddRow(RenderRow(index, state, unicode));
        }

        var rows = new List<IRenderable> { new Markup(title), new Text(string.Empty) };
        rows.Add(state.Offset > 0 ? new Markup("[dim]  ↑ more[/]") : new Text(string.Empty));
        rows.Add(grid);
        rows.Add(lastVisible < state.Count ? new Markup("[dim]  ↓ more[/]") : new Text(string.Empty));
        rows.Add(new Markup("[dim]↑/↓ move · space select · a select all · enter confirm · esc cancel[/]"));
        if (Hint is not null)
        {
            rows.Add(new Markup($"[dim]{Hint}[/]"));
        }

        return new Rows(rows);
    }

    private Markup[] RenderRow(int index, MultiSelectState state, bool unicode)
    {
        var isCursor = index == state.Cursor;
        var style = !state.IsSelectable(index) ? "grey"
            : isCursor ? $"{Widgets.PrimaryColorMarkup} bold"
            : "default";

        var pointer = isCursor ? (unicode ? "❯" : ">") : " ";
        var checkbox = state.IsSelected(index) ? (unicode ? "◉" : "[[x]]") : (unicode ? "○" : "[[ ]]");

        var cells = columns(items[index]).Select((text, column) =>
        {
            // Only the first column (the name) is highlighted; the rest stay dim to keep the list calm.
            var cellStyle = column == 0 || !state.IsSelectable(index) ? style : "dim";
            return new Markup($"[{cellStyle}]{text.EscapeMarkup()}[/]");
        });

        return [new Markup($"[{style}]{pointer} {checkbox}[/]"), .. cells];
    }
}
