using Pubwiz.Tool.Rendering;
using Spectre.Console.Rendering;

namespace Pubwiz.Tool.Prompts;

/// <summary>
/// Chooses the order a batch is published in: each item gets a number, lower numbers go first, and items with the same
/// number are published at the same time. The rows are kept sorted by number, with a bracket joining each step.
/// </summary>
/// <remarks>
/// Keys: ↑/↓ move, a digit sets the number, ←/→ (or -/+) make it earlier or later, A puts everything at the same time,
/// O numbers everything one after another, Enter confirms, Esc throws <see cref="PromptCancelledException"/>.
/// </remarks>
/// <param name="title">The question (markup).</param>
/// <param name="items">The items, in their own order.</param>
/// <param name="columns">The item's columns as plain text; the first one is its name.</param>
public sealed class OrderPrompt<T>(string title, IReadOnlyList<T> items, Func<T, IReadOnlyList<string>> columns) : IPrompt<IReadOnlyList<int>>
    where T : notnull
{
    /// <summary>Rows used by the title, explanation, scroll hints, plan and keys, subtracted from the terminal height to get the page size.</summary>
    private const int ChromeHeight = 10;

    /// <summary>The starting number of each item; all 1 (everything at the same time) by default.</summary>
    public IReadOnlyList<int>? Orders { get; init; }

    /// <summary>The most items published at the same time, if limited (e.g. by <c>--parallel</c>), mentioned in the plan.</summary>
    public int? Parallel { get; init; }

    /// <summary>What the numbers mean, shown under the title (markup).</summary>
    public string Explanation { get; init; } = "Lower numbers are published first. The same number means at the same time.";

    public IReadOnlyList<int> Show(IAnsiConsole console) => ShowAsync(console, CancellationToken.None).GetAwaiter().GetResult();

    /// <returns>The step of each item (1, 2, ... without gaps), in item order.</returns>
    public async Task<IReadOnlyList<int>> ShowAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        var state = new OrderState(Orders ?? Enumerable.Repeat(1, items.Count).ToList(), console.Profile.Height - ChromeHeight);

        console.Cursor.Hide();
        try
        {
            return await console.Live(Render(state, console)).AutoClear(true).StartAsync(async context =>
            {
                context.Refresh();
                while (true)
                {
                    var key = await console.Input.ReadKeyAsync(intercept: true, cancellationToken);
                    if (key is not null && HandleKey(key.Value, state))
                    {
                        return state.Steps;
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

    /// <returns><c>true</c> when the order is confirmed.</returns>
    private static bool HandleKey(ConsoleKeyInfo key, OrderState state)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                state.Move(-1);
                break;
            case ConsoleKey.DownArrow or ConsoleKey.Tab:
                state.Move(1);
                break;
            case ConsoleKey.Home:
                state.MoveTo(0);
                break;
            case ConsoleKey.End:
                state.MoveTo(state.Count - 1);
                break;
            case ConsoleKey.LeftArrow or ConsoleKey.Subtract or ConsoleKey.OemMinus:
                state.Change(-1);
                break;
            case ConsoleKey.RightArrow or ConsoleKey.Add or ConsoleKey.OemPlus:
                state.Change(1);
                break;
            case ConsoleKey.Enter:
                return true;
            case ConsoleKey.Escape:
                throw new PromptCancelledException();
            default:
                // By character rather than key, so it works the same on every keyboard layout.
                switch (char.ToLowerInvariant(key.KeyChar))
                {
                    case var digit when char.IsAsciiDigit(digit):
                        state.Type(digit - '0');
                        break;
                    case '+':
                        state.Change(1);
                        break;
                    case 'a':
                        state.AllAtOnce();
                        break;
                    case 'o':
                        state.OneByOne();
                        break;
                }

                break;
        }

        return false;
    }

    private IRenderable Render(OrderState state, IAnsiConsole console)
    {
        var unicode = console.Profile.Capabilities.Unicode;
        var columnCount = columns(items[0]).Count;
        var numberWidth = state.Count.ToString().Length;

        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(1)); // pointer
        grid.AddColumn(new GridColumn().NoWrap().PadRight(1)); // number
        grid.AddColumn(new GridColumn().NoWrap().PadRight(1)); // bracket
        for (var i = 0; i < columnCount; i++)
        {
            grid.AddColumn(new GridColumn().NoWrap().PadRight(3));
        }

        var lastRow = Math.Min(state.Offset + state.PageSize, state.Count);
        for (var row = state.Offset; row < lastRow; row++)
        {
            grid.AddRow(RenderRow(state, row, numberWidth, unicode));
        }

        var steps = state.Steps;
        var plan = PublishPlan.Describe(Enumerable.Range(0, state.Count).Select(i => (steps[i], columns(items[i])[0])).ToList(), Parallel);
        var stepCount = state.StepCount == 1 ? "1 step" : $"{state.StepCount} steps";

        return new Rows(
            new Markup(title),
            new Markup($"[dim]{Explanation}[/]"),
            state.Offset > 0 ? new Markup("[dim]  ↑ more[/]") : Text.Empty,
            grid,
            lastRow < state.Count ? new Markup("[dim]  ↓ more[/]") : Text.Empty,
            new Markup($"  [bold]{stepCount}:[/] {plan.EscapeMarkup()}"),
            PublishPlan.TooManyAtOnce(steps, Parallel) ? new Markup($"  {PublishPlan.TooManyAtOnceTip}") : Text.Empty,
            Text.Empty,
            RenderKeys(state));
    }

    private IRenderable[] RenderRow(OrderState state, int row, int numberWidth, bool unicode)
    {
        var item = state.Rows[row];
        var isCursor = item == state.CursorItem;
        var order = state.Orders[item];

        // The place of the row in its step, for the bracket.
        var stepStart = row;
        while (stepStart > 0 && state.Orders[state.Rows[stepStart - 1]] == order)
        {
            stepStart--;
        }

        var pointer = isCursor ? $"[{Widgets.PrimaryColorMarkup} bold]{(unicode ? "❯" : ">")}[/]" : " ";
        var number = order.ToString().PadLeft(numberWidth);
        var numberCell = isCursor ? $"[{Widgets.HighlightMarkup}] {number} [/]" : $"[{Widgets.PrimaryColorMarkup}] {number} [/]";
        var bracket = $"[{Widgets.PrimaryColorMarkup} dim]{PublishPlan.Connector(row - stepStart, state.StepSize(item), unicode)}[/]";

        var cells = columns(items[item]).Select((text, column) =>
            new Markup(column == 0 ? $"[{(isCursor ? "bold" : "default")}]{text.EscapeMarkup()}[/]" : $"[{(isCursor ? "default" : "dim")}]{text.EscapeMarkup()}[/]"));

        return [new Markup(pointer), new Markup(numberCell), new Markup(bracket), .. cells];
    }

    private static Markup RenderKeys(OrderState state)
    {
        (string Key, string Action)[] keys =
        [
            ("↑/↓", "move"),
            (state.Count <= 9 ? $"1-{state.Count}" : "type a number", "set"),
            ("←/→", "earlier/later"),
            ("a", "all at once"),
            ("o", "one by one"),
            ("enter", "confirm"),
            ("esc", "back")
        ];

        return new Markup(string.Join(" [dim]·[/] ", keys.Select(k => $"[bold]{k.Key}[/] [dim]{k.Action}[/]")));
    }
}
