namespace Pubwiz.Core.Infrastructure.Processes;

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

    /// <summary>
    /// Runs a process attached to the terminal (nothing is captured), for commands that talk to the user
    /// directly: <c>az login</c>, <c>sudo</c> install commands, password prompts, ...
    /// </summary>
    /// <returns>The exit code.</returns>
    /// <exception cref="ProcessStartException">The executable could not be started.</exception>
    Task<int> RunInteractiveAsync(ProcessSpec spec, CancellationToken cancellationToken = default);
}
