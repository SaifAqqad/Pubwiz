namespace Pubwiz.Tool.Prompts;

/// <summary>
/// The wizard's questions, so every page looks the same:
/// <list type="bullet">
///   <item>the question in bold, a hint in dim parentheses and the default in dim brackets: <c>Port [21]:</c></item>
///   <item>yes/no questions end with <c>[Y/n]</c> or <c>[y/N]</c> (the capital letter is the default)</item>
///   <item>answers are shown in the primary color</item>
///   <item>list choices are written back once picked (<c>Encryption: FTPS</c>), so every answer stays on screen</item>
/// </list>
/// Questions and hints are plain text (they're escaped); list items may use markup.
/// </summary>
public static class Questions
{
    private static Style AnswerStyle { get; } = new(Widgets.PrimaryColor);

    /// <summary>Asks for text.</summary>
    /// <param name="console">The console.</param>
    /// <param name="question">e.g. "Server".</param>
    /// <param name="hint">Extra help, e.g. "e.g. iis.contoso.com".</param>
    /// <param name="defaultValue">Used when the answer is left empty.</param>
    /// <param name="optional">Allow an empty answer (returned as an empty string).</param>
    /// <param name="validate">Returns an error message, or <c>null</c> when the answer is fine.</param>
    public static string Ask(this IAnsiConsole console, string question, string? hint = null, string? defaultValue = null,
        bool optional = false, Func<string, string?>? validate = null)
    {
        var prompt = new TextPrompt<string>(Label(question, hint, defaultValue) + ":")
            .PromptStyle(AnswerStyle)
            .ShowDefaultValue(false);

        if (defaultValue is not null)
        {
            prompt.DefaultValue(defaultValue);
        }

        if (optional)
        {
            prompt.AllowEmpty();
        }

        if (validate is not null)
        {
            prompt.Validate(answer => validate(answer) is { } error ? ValidationResult.Error($"[red]{error.EscapeMarkup()}[/]") : ValidationResult.Success());
        }

        return console.Prompt(prompt);
    }

    /// <summary>Asks for a number, e.g. a port.</summary>
    public static int AskNumber(this IAnsiConsole console, string question, int defaultValue, string? hint = null) =>
        console.Prompt(new TextPrompt<int>(Label(question, hint, defaultValue.ToString()) + ":")
            .PromptStyle(AnswerStyle)
            .ShowDefaultValue(false)
            .DefaultValue(defaultValue));

    /// <summary>Asks for a password; the typed characters are masked, and Esc throws <see cref="PromptCancelledException"/>.</summary>
    public static string AskSecret(this IAnsiConsole console, string question, string? hint = null, Func<string, string?>? validate = null)
    {
        var fullHint = hint is null ? "Esc to cancel" : $"{hint} · Esc to cancel";
        return console.Prompt(new LinePrompt(Label(question, fullHint, defaultValue: null) + ":") { Secret = true, Validate = validate, AnswerStyle = AnswerStyle });
    }

    /// <summary>Asks for text like <see cref="Ask"/>, but Esc throws <see cref="PromptCancelledException"/>, so it can be backed out of.</summary>
    public static string AskOrCancel(this IAnsiConsole console, string question, string? hint = null, Func<string, string?>? validate = null)
    {
        var fullHint = hint is null ? "Esc to cancel" : $"{hint} · Esc to cancel";
        return console.Prompt(new LinePrompt(Label(question, fullHint, defaultValue: null) + ":") { Validate = validate, AnswerStyle = AnswerStyle });
    }

    /// <summary>Asks a yes/no question, e.g. "Use passive mode? [Y/n]".</summary>
    public static bool AskYesNo(this IAnsiConsole console, string question, bool defaultValue, string? hint = null)
    {
        var choices = defaultValue ? "[[Y/n]]" : "[[y/N]]";
        var label = $"{Label(question, hint, defaultValue: null)} [dim]{choices}[/]";

        return console.Prompt(new ConfirmationPrompt(label) { DefaultValue = defaultValue, ShowChoices = false, ShowDefaultValue = false });
    }

    /// <summary>Picks one item from a list, then writes the question with the chosen answer.</summary>
    /// <param name="console">The console.</param>
    /// <param name="question">e.g. "Encryption".</param>
    /// <param name="choices">The items, in display order.</param>
    /// <param name="describe">How an item is listed (markup); defaults to its text.</param>
    public static T Choose<T>(this IAnsiConsole console, string question, IEnumerable<T> choices, Func<T, string>? describe = null)
        where T : notnull
    {
        describe ??= item => item.ToString()!.EscapeMarkup();

        var answer = console.Prompt(Menu<T>($"[bold]{question.EscapeMarkup()}[/]")
            .AddChoices(choices)
            .UseConverter(describe));

        WriteAnswer(console, question, describe(answer));
        return answer;
    }

    /// <summary>
    /// Picks one item from a list loaded from somewhere else (e.g. the apps in an Azure subscription), then writes the
    /// question with the chosen answer. The list ends with a "Refresh" row that loads it again, for things created
    /// while the wizard is open. Typing searches, with names that start with the text listed first.
    /// When nothing is found, <paramref name="emptyMessage"/> is shown with only Refresh to pick.
    /// Esc throws <see cref="PromptCancelledException"/>.
    /// </summary>
    /// <param name="console">The console.</param>
    /// <param name="question">e.g. "App Service app".</param>
    /// <param name="loadingText">Shown with a spinner while loading, e.g. "Loading apps...".</param>
    /// <param name="load">Loads the items.</param>
    /// <param name="columns">The item's columns as plain text; the first one (the name) is searched first and written back as the answer.</param>
    /// <param name="emptyMessage">Why the list is empty and what to do about it (plain text).</param>
    /// <param name="cancellationToken">Cancels loading.</param>
    public static async Task<T> ChooseWithRefreshAsync<T>(this IAnsiConsole console, string question, string loadingText,
        Func<CancellationToken, Task<IReadOnlyList<T>>> load, Func<T, IReadOnlyList<string>> columns, string emptyMessage,
        CancellationToken cancellationToken)
        where T : notnull
    {
        const string refresh = "↻ Refresh the list";
        while (true)
        {
            var items = await console.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(AnswerStyle)
                .StartAsync(loadingText, _ => load(cancellationToken));

            if (items.Count == 0)
            {
                console.Write(Widgets.Warning(emptyMessage));
                console.WriteLine();
                console.Prompt(Menu<string>($"[bold]{question.EscapeMarkup()}[/]").AddChoices(refresh).UseConverter(text => $"[dim]{text}[/]"));
                continue;
            }

            var prompt = new MultiSelectPrompt<T>($"[bold]{question.EscapeMarkup()}[/]", items, columns)
            {
                SingleSelect = true,
                ExitLabel = "Refresh the list",
                ExitIcon = ("↻", "~"),
                EscapeChoosesExitRow = false
            };

            if (await prompt.ShowAsync(console, cancellationToken) is { Highlighted: var chosen })
            {
                WriteAnswer(console, question, columns(chosen)[0].EscapeMarkup());
                return chosen;
            }
        }
    }

    /// <summary>A list disappears once answered, so its answer is written the way text answers look.</summary>
    private static void WriteAnswer(IAnsiConsole console, string question, string answerMarkup)
    {
        var separator = question.EndsWith('?') ? " " : ": ";
        console.MarkupLine($"[bold]{question.EscapeMarkup()}[/]{separator}[{Widgets.PrimaryColorMarkup}]{Markup.Remove(answerMarkup).EscapeMarkup()}[/]");
    }

    /// <summary>
    /// A list to pick one item from, with the wizard's look: the highlighted item in the primary color, and typing searches.
    /// Esc throws <see cref="PromptCancelledException"/>; lists with a "back" option use <c>AddCancelResult</c> to pick it instead.
    /// </summary>
    /// <param name="title">The title, as markup.</param>
    public static SelectionPrompt<T> Menu<T>(string title)
        where T : notnull
    {
        return new SelectionPrompt<T>()
            .Title(title)
            .HighlightStyle(AnswerStyle)
            .PageSize(15)
            .EnableSearch()
            .SearchPlaceholderText("[dim]Type to search[/]")
            .AddCancelResult(() => throw new PromptCancelledException());
    }

    /// <summary>A heading line that starts a group of questions, e.g. "FTP / FTPS".</summary>
    public static void Section(this IAnsiConsole console, string title)
    {
        console.WriteLine();
        console.Write(new Rule($"[{Widgets.PrimaryColorMarkup} bold]{title.EscapeMarkup()}[/]")
            .LeftJustified()
            .RuleStyle(new Style(Widgets.PrimaryColor, decoration: Decoration.Dim)));
    }

    private static string Label(string question, string? hint, string? defaultValue)
    {
        var label = $"[bold]{question.EscapeMarkup()}[/]";
        if (hint is not null)
        {
            label += $" [dim]({hint.EscapeMarkup()})[/]";
        }

        if (defaultValue is not null)
        {
            label += $" [dim][[{defaultValue.EscapeMarkup()}]][/]";
        }

        return label;
    }
}
