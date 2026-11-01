namespace Pubwiz.Tool.Prompts;

/// <summary>
/// Asks for a password, showing <c>*</c> for each typed character. Unlike Spectre's secret <see cref="TextPrompt{T}"/>,
/// Esc cancels it (with <see cref="PromptCancelledException"/>).
/// </summary>
/// <remarks>
/// Keys: Enter accepts (an empty answer is ignored), Backspace deletes, Esc cancels. Ctrl+C cancels too where
/// <see cref="Infrastructure.InterruptibleConsole"/> makes it stop the key read.
/// </remarks>
/// <param name="label">The question, as markup, e.g. <c>[bold]Password for deploy[/]:</c>.</param>
public sealed class SecretPrompt(string label) : IPrompt<string>
{
    private const char Mask = '*';

    /// <summary>Returns an error message (plain text), or <c>null</c> when the answer is fine.</summary>
    public Func<string, string?>? Validate { get; init; }

    /// <summary>How the mask characters are drawn.</summary>
    public Style MaskStyle { get; init; } = Style.Plain;

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

                case ConsoleKey.Enter when answer.Length > 0:
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
                        console.Write(new Text(Mask.ToString(), MaskStyle));
                    }

                    break;
            }
        }
    }
}
