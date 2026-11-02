using Pubwiz.Core.Infrastructure.Processes;

namespace Pubwiz.Core.Tests.TestHelpers;

/// <summary>
/// Stands in for an external tool: each run is answered by <paramref name="respond"/>, which gets the arguments and
/// returns the tool's output (or throws <see cref="ScriptedFailure"/> to fail the run). Every run is recorded.
/// </summary>
public sealed class ScriptedProcessRunner(Func<IReadOnlyList<string>, string> respond) : IProcessRunner
{
    public List<IReadOnlyList<string>> Runs { get; } = [];

    public Task<ProcessResult> RunAsync(ProcessSpec spec, Action<ProcessOutputLine>? onOutput = null, CancellationToken cancellationToken = default)
    {
        Runs.Add(spec.Arguments);
        try
        {
            return Task.FromResult(new ProcessResult(0, respond(spec.Arguments), string.Empty));
        }
        catch (ScriptedFailure failure)
        {
            return Task.FromResult(new ProcessResult(1, string.Empty, failure.Message));
        }
    }

    public Task<int> RunInteractiveAsync(ProcessSpec spec, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <summary>Thrown by a response to make the run fail with this message on stderr.</summary>
    public sealed class ScriptedFailure(string message) : Exception(message);
}
