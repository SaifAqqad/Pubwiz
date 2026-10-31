namespace Pubwiz.Core.Infrastructure;

/// <summary>
/// Thrown when a tool Pubwiz needs (dotnet, az, docker, ...) isn't installed or can't be found.
/// </summary>
/// <param name="toolName">The missing tool, e.g. <c>dotnet</c>.</param>
/// <param name="hint">What the user can do about it.</param>
public sealed class PrerequisiteMissingException(string toolName, string hint)
    : Exception($"The {toolName} CLI was not found. {hint}")
{
    public string ToolName { get; } = toolName;

    public string Hint { get; } = hint;
}
