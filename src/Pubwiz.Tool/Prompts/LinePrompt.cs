namespace Pubwiz.Tool.Prompts;

/// <summary>
/// Asks for one line of text, optionally masked (<c>*</c> per character, for passwords). Unlike Spectre's
/// <see cref="TextPrompt{T}"/>, Esc cancels it (with <see cref="PromptCancelledException"/>).
/// </summary>
/// <remarks>
/// Keys: Enter accepts (an empty answer is ignored unless <see cref="AllowEmpty"/>), Backspace deletes, Esc cancels.
/// Ctrl+C cancels too where <see cref="Infrastructure.InterruptibleConsole"/> makes it stop the key read.
/// </remarks>
/// <param name="label">The question, as markup, e.g. <c>[bold]Password for deploy[/]:</c>.</param>
public sealed class LinePrompt(string label) : IPrompt<string>
{
    private const char Mask = '*';

    /// <summary>Show <c>*</c> instead of the typed characters.</summary>
    public bool Secret { get; init; }

    /// <summary>Accept an empty answer.</summary>
    public bool AllowEmpty { get; init; }

    /// <summary>Returns an error message (plain text), or <c>null</c> when the answer is fine.</summary>
    public Func<string, string?>? Validate { get; init; }

    /// <summary>How the typed characters (or masks) are drawn.</summary>
    public Style AnswerStyle { get; init; } = Style.Plain;

    public string Show(IAnsiConsole console) => ShowAsync(console, CancellationToken.None).GetAwaiter().GetResult();

    public async Task<string> ShowAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        while (true)
        {
            console.Markup(label + " ");
            var answer = await ReadAsync(console, cancellationToken);

            if (Validate?.Invoke(answer) is not { } error)
            {
                return answer;
            }

            console.MarkupLine($"[red]{error.EscapeMarkup()}[/]");
        }
    }

    private async Task<string> ReadAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        var answer = new System.Text.StringBuilder();
        while (true)
        {
            if (await console.Input.ReadKeyAsync(intercept: true, cancellationToken) is not { } key)
            {
                continue;
            }

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    console.WriteLine();
                    throw new PromptCancelledException();

                case ConsoleKey.Enter when answer.Length > 0 || AllowEmpty:
                    console.WriteLine();
                    return answer.ToString();

                case ConsoleKey.Enter:
                    break;

                case ConsoleKey.Backspace when answer.Length > 0:
                    answer.Length--;
                    console.Write("\b \b");
                    break;

                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        answer.Append(key.KeyChar);
                        console.Write(new Text(Secret ? Mask.ToString() : key.KeyChar.ToString(), AnswerStyle));
                    }

                    break;
            }
        }
    }
}
