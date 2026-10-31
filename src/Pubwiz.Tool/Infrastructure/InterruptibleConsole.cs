using Spectre.Console.Rendering;

namespace Pubwiz.Tool.Infrastructure;

/// <summary>
/// The console, except that a prompt waiting for a key stops when Ctrl+C is pressed inside a
/// <see cref="CtrlC.CancelInstead"/> scope.
/// </summary>
/// <remarks>
/// Pubwiz shows prompts with the synchronous <c>console.Prompt(...)</c>, which takes no cancellation token.
/// Spectre's prompts all wait for keys with <see cref="IAnsiConsoleInput.ReadKeyAsync"/> though, so cancelling that
/// key read makes the waiting prompt throw an <see cref="OperationCanceledException"/>. The wizard uses it to go
/// back to its main menu on Ctrl+C. Everything else is passed to the real console as-is.
/// </remarks>
public sealed class InterruptibleConsole(IAnsiConsole console) : IAnsiConsole
{
    public Profile Profile => console.Profile;

    public IAnsiConsoleCursor Cursor => console.Cursor;

    public IAnsiConsoleInput Input { get; } = new InterruptibleInput(console.Input);

    public IExclusivityMode ExclusivityMode => console.ExclusivityMode;

    public RenderPipeline Pipeline => console.Pipeline;

    public void Clear(bool home) => console.Clear(home);

    public void Write(IRenderable renderable) => console.Write(renderable);

    public void WriteAnsi(Action<AnsiWriter> action) => console.WriteAnsi(action);

    private sealed class InterruptibleInput(IAnsiConsoleInput input) : IAnsiConsoleInput
    {
        public bool IsKeyAvailable() => input.IsKeyAvailable();

        public ConsoleKeyInfo? ReadKey(bool intercept) => input.ReadKey(intercept);

        public async Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, CtrlC.ScopeToken);
            return await input.ReadKeyAsync(intercept, cancellation.Token);
        }
    }
}
