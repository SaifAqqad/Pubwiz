namespace Pubwiz.Core.Processes;

/// <summary>
/// Runs external processes. Every CLI wrapper in Pubwiz goes through this,
/// so output can be streamed to the UI and the run can be faked in tests.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs a process to completion.
    /// </summary>
    /// <param name="spec">What to run.</param>
    /// <param name="onOutput">Called for every output line as it arrives (optional).</param>
    /// <param name="cancellationToken">Cancelling kills the process and all of its children.</param>
    /// <exception cref="ProcessStartException">The executable could not be started.</exception>
    Task<ProcessResult> RunAsync(ProcessSpec spec, Action<ProcessOutputLine>? onOutput = null, CancellationToken cancellationToken = default);
}
