using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Targets.Azure;

namespace Pubwiz.Core.Tests.TestHelpers;

/// <summary>
/// Answers az commands with fixed output, matched by how the command starts (e.g. <c>account show</c>),
/// so <see cref="AzCli"/> can be tested without az or an Azure account.
/// </summary>
internal sealed class FakeAzRunner : IProcessRunner
{
    private readonly List<(string Command, ProcessResult Result, bool Once)> _answers = [];

    /// <summary>The commands that ran, without "az", e.g. <c>account show -o json</c>.</summary>
    public List<string> Commands { get; } = [];

    /// <summary>The contents of the files passed as <c>@path</c> arguments (e.g. <c>az rest --body @path</c>), read while the command ran.</summary>
    public List<string> RequestBodies { get; } = [];

    /// <summary>Answers every command that starts with <paramref name="command"/>.</summary>
    public FakeAzRunner Answer(string command, string output)
    {
        _answers.Add((command, new ProcessResult(0, output, string.Empty), Once: false));
        return this;
    }

    /// <summary>Answers the next command that starts with <paramref name="command"/>, e.g. one page of results.</summary>
    public FakeAzRunner AnswerOnce(string command, string output)
    {
        _answers.Add((command, new ProcessResult(0, output, string.Empty), Once: true));
        return this;
    }

    public AzCli CreateAzCli() => new(this, "az");

    public Task<ProcessResult> RunAsync(ProcessSpec spec, Action<ProcessOutputLine>? onOutput = null, CancellationToken cancellationToken = default)
    {
        var command = string.Join(' ', spec.Arguments);
        Commands.Add(command);
        RequestBodies.AddRange(spec.Arguments.Where(a => a.StartsWith('@') && File.Exists(a[1..])).Select(a => File.ReadAllText(a[1..])));

        var index = _answers.FindIndex(a => command.StartsWith(a.Command, StringComparison.Ordinal));
        if (index < 0)
        {
            return Task.FromResult(new ProcessResult(1, string.Empty, $"no answer for 'az {command}'"));
        }

        var answer = _answers[index];
        if (answer.Once)
        {
            _answers.RemoveAt(index);
        }

        return Task.FromResult(answer.Result);
    }

    public Task<int> RunInteractiveAsync(ProcessSpec spec, CancellationToken cancellationToken = default) => Task.FromResult(1);
}
