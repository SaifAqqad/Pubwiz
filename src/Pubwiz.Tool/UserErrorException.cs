namespace Pubwiz.Tool;

/// <summary>
/// An expected problem caused by the input or the workspace (nothing to publish, invalid path, ...).
/// It's shown as a plain error message, without a stack trace.
/// </summary>
public sealed class UserErrorException(string message, int exitCode = ExitCodes.InvalidInput) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}
