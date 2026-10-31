namespace Pubwiz.Tool.Infrastructure;

/// <summary>
/// Handles Ctrl+C for the whole app (<see cref="Console.CancelKeyPress"/> is process-wide, so there's one handler).
/// </summary>
/// <remarks>
/// By default Ctrl+C ends Pubwiz (exit code 130), but first restores the terminal: a prompt or menu can leave the
/// cursor hidden and in the middle of a line, which would put the shell prompt right after it. While publishing, the first
/// Ctrl+C cancels the jobs instead (see <see cref="CancelInstead"/>), so results can still be shown.
/// </remarks>
public static class CtrlC
{
    private static readonly Lock Gate = new();
    private static CancellationTokenSource? _cancellation;
    private static IAnsiConsole? _console;

    /// <summary>Starts handling Ctrl+C. Call once at startup.</summary>
    public static void Install(IAnsiConsole console)
    {
        _console = console;
        Console.CancelKeyPress += OnCancelKeyPress;
    }

    /// <summary>
    /// Until the returned scope is disposed, the first Ctrl+C cancels <paramref name="cancellation"/> instead of
    /// ending Pubwiz. A second Ctrl+C still ends it.
    /// </summary>
    public static IDisposable CancelInstead(CancellationTokenSource cancellation)
    {
        lock (Gate)
        {
            _cancellation = cancellation;
        }

        return new Scope();
    }

    /// <summary>Shows the cursor and ends the current line, so whatever comes next starts cleanly.</summary>
    /// <remarks>
    /// Written straight to the output stream: while a menu is open, Spectre passes console writes through the
    /// menu's renderer, which would print the new line above the menu and then draw the menu again below it.
    /// </remarks>
    public static void RestoreTerminal()
    {
        if (_console is null)
        {
            return;
        }

        var output = _console.Profile.Out.Writer;
        if (_console.Profile.Capabilities.Ansi)
        {
            const string showCursor = "\u001b[?25h";
            output.Write(showCursor);
        }

        output.WriteLine();
        output.Flush();
    }

    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        lock (Gate)
        {
            if (_cancellation is { IsCancellationRequested: false } cancellation)
            {
                e.Cancel = true;
                cancellation.Cancel();
                return;
            }
        }

        // Exit here instead of letting the runtime end the process, so the exit code is 130 on every OS
        // (Windows would otherwise report 0xC000013A).
        e.Cancel = true;
        RestoreTerminal();
        Environment.Exit(ExitCodes.Cancelled);
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose()
        {
            lock (Gate)
            {
                _cancellation = null;
            }
        }
    }
}
