using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Targets.Azure;

namespace Pubwiz.Core.Tests.TestHelpers;

/// <summary>
/// Answers az commands with fixed output, matched by how the command starts (e.g. <c>account show</c>),
/// so <see cref="AzCli"/> can be tested without az or an Azure account.
/// </summary>
internal sealed class FakeAzRunner : IProcessRunner
{
    private readonly List<(string Command, ProcessResult Result)> _answers = [];

    /// <summary>The commands that ran, without "az", e.g. <c>account show -o json</c>.</summary>
    public List<string> Commands { get; } = [];

    public FakeAzRunner Answer(string command, string output)
    {
        _answers.Add((command, new ProcessResult(0, output, string.Empty)));
        return this;
    }

    public AzCli CreateAzCli() => new(this, "az");

    public Task<ProcessResult> RunAsync(ProcessSpec spec, Action<ProcessOutputLine>? onOutput = null, CancellationToken cancellationToken = default)
    {
        var command = string.Join(' ', spec.Arguments);
        Commands.Add(command);

        var answer = _answers.FirstOrDefault(a => command.StartsWith(a.Command, StringComparison.Ordinal));
        return Task.FromResult(answer.Result ?? new ProcessResult(1, string.Empty, $"no answer for 'az {command}'"));
    }

    public Task<int> RunInteractiveAsync(ProcessSpec spec, CancellationToken cancellationToken = default) => Task.FromResult(1);
}
