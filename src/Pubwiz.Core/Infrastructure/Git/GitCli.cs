using Pubwiz.Core.Infrastructure.Platform;
using Pubwiz.Core.Infrastructure.Processes;
using System.Collections.Concurrent;
using System.Globalization;

namespace Pubwiz.Core.Infrastructure.Git;

/// <summary>
/// Reads the branch and last commit of a folder's git repository.
/// </summary>
/// <remarks>
/// Git is optional: when it isn't installed, the folder isn't in a repository, or the repository has no commits yet,
/// <see cref="GetInfoAsync"/> returns <c>null</c>. Results are cached per repository for the rest of the run.
/// </remarks>
public sealed class GitCli(IProcessRunner processRunner)
{
    /// <summary>Separates the fields of the <c>git log</c> output (the ASCII unit separator, which never appears in commit text).</summary>
    private const char FieldSeparator = '\u001f';

    private readonly ConcurrentDictionary<string, Task<GitInfo?>> _byRepository = new(StringComparer.Ordinal);

    private readonly Lazy<string?> _executable = new(() => ExecutableLocator.FindInPath("git"));

    /// <param name="folder">Any folder inside the repository.</param>
    /// <param name="cancellationToken">Cancels the git commands.</param>
    /// <returns>The repository's state, or <c>null</c> when there's no git repository (or no git).</returns>
    public async Task<GitInfo?> GetInfoAsync(string folder, CancellationToken cancellationToken = default)
    {
        if (_executable.Value is not { } git || !Directory.Exists(folder))
        {
            return null;
        }

        try
        {
            var root = await RunAsync(git, folder, ["rev-parse", "--show-toplevel"], cancellationToken);
            if (root is null)
            {
                return null;
            }

            var repositoryRoot = Path.GetFullPath(root.Trim());
            return await _byRepository.GetOrAdd(repositoryRoot, _ => ReadAsync(git, repositoryRoot, cancellationToken));
        }
        catch (ProcessStartException)
        {
            return null;
        }
    }

    private async Task<GitInfo?> ReadAsync(string git, string repositoryRoot, CancellationToken cancellationToken)
    {
        var branch = RunAsync(git, repositoryRoot, ["symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken);
        var log = RunAsync(git, repositoryRoot, ["log", "-1", "--format=%H%x1f%h%x1f%cI%x1f%an%x1f%s"], cancellationToken);
        var status = RunAsync(git, repositoryRoot, ["status", "--porcelain"], cancellationToken);
        await Task.WhenAll(branch, log, status);

        // No commits yet (or a broken repository): nothing worth showing.
        if (await log is not { } logOutput || ParseLog(logOutput) is not { } last)
        {
            return null;
        }

        var branchName = (await branch)?.Trim();
        return new GitInfo(
            repositoryRoot,
            string.IsNullOrEmpty(branchName) ? null : branchName,
            last.Commit,
            last.ShortCommit,
            last.Subject,
            last.Author,
            last.Date,
            HasChanges: !string.IsNullOrWhiteSpace(await status));
    }

    /// <summary>Parses the <c>%H %h %cI %an %s</c> fields of <c>git log -1</c>.</summary>
    internal static (string Commit, string ShortCommit, DateTimeOffset Date, string Author, string Subject)? ParseLog(string output)
    {
        var fields = output.TrimEnd('\r', '\n').Split(FieldSeparator, 5);
        if (fields.Length < 5 || !DateTimeOffset.TryParse(fields[2], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return null;
        }

        return (fields[0], fields[1], date, fields[3], fields[4]);
    }

    /// <returns>The output, or <c>null</c> when the command failed.</returns>
    private async Task<string?> RunAsync(string git, string folder, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var spec = new ProcessSpec(git, ["-C", folder, .. arguments])
        {
            // Never stop to ask for anything (credentials, pager, ...).
            Environment = new Dictionary<string, string?> { ["GIT_TERMINAL_PROMPT"] = "0", ["GIT_PAGER"] = "cat", ["GIT_OPTIONAL_LOCKS"] = "0" }
        };

        var result = await processRunner.RunAsync(spec, cancellationToken: cancellationToken);
        return result.Succeeded ? result.Output : null;
    }
}
