namespace Pubwiz.Core.Infrastructure.Processes;

/// <summary>
/// Thrown when an external process cannot be started, usually because the executable is missing.
/// </summary>
public sealed class ProcessStartException(string fileName, Exception? innerException = null)
    : Exception($"Failed to start '{fileName}'. Make sure it is installed and available in PATH.", innerException)
{
    public string FileName { get; } = fileName;
}
