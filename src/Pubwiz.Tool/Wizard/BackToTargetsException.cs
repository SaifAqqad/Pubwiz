namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Thrown by a target's settings page to go back to the list of targets, e.g. when the user chooses
/// "Choose another target" because something the target needs isn't available.
/// </summary>
public sealed class BackToTargetsException() : Exception("Back to the targets.");
