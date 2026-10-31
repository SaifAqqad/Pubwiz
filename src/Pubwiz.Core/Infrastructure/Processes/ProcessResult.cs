namespace Pubwiz.Core.Infrastructure.Processes;

/// <summary>
/// The outcome of a finished process.
/// </summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="Output">Everything written to standard output.</param>
/// <param name="Error">Everything written to standard error.</param>
public sealed record ProcessResult(int ExitCode, string Output, string Error)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// A single line written by a running process.
/// </summary>
/// <param name="Text">The line, without the trailing newline.</param>
/// <param name="IsError"><c>true</c> when the line came from standard error.</param>
public readonly record struct ProcessOutputLine(string Text, bool IsError);
