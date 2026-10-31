namespace Pubwiz.Tool.Prompts;

/// <summary>
/// Thrown when Esc is pressed in a list that has no "back" option of its own.
/// </summary>
/// <remarks>
/// The wizard catches it to go back to its main menu. Anywhere else it ends Pubwiz as cancelled,
/// because it's an <see cref="OperationCanceledException"/>.
/// </remarks>
public sealed class PromptCancelledException() : OperationCanceledException("Cancelled with Esc.");
