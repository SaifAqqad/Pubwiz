namespace Pubwiz.Tool;

/// <summary>
/// Process exit codes returned by the tool.
/// </summary>
public static class ExitCodes
{
    public const int Success = 0;

    /// <summary>At least one publish failed.</summary>
    public const int PublishFailed = 1;

    /// <summary>A required tool (dotnet, az, docker, ...) is missing.</summary>
    public const int MissingPrerequisite = 2;

    /// <summary>Invalid input, or nothing to publish.</summary>
    public const int InvalidInput = 3;

    /// <summary>The user cancelled (Ctrl+C or Esc).</summary>
    public const int Cancelled = 130;

    /// <summary>An unexpected error.</summary>
    public const int UnhandledError = -1;
}
