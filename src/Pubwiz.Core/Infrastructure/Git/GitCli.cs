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
/// <para>
/// To know about commits that haven't been pulled, <see cref="FetchAsync"/> fetches the branch's remote in the background.
/// The fetch never asks for credentials and gives up after <see cref="FetchTimeout"/>; the counts are then as of the last fetch.
/// </para>
/// </remarks>
public sealed class GitCli(IProcessRunner processRunner)
{
    /// <summary>Separates the fields of the <c>git log</c> output (the ASCII unit separator, which never appears in commit text).</summary>
    private const char FieldSeparator = '\u001f';

    /// <summary>How long a fetch may take before it's abandoned.</summary>
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(8);

    private readonly ConcurrentDictionary<string, Task<GitInfo?>> _byRepository = new(StringComparer.Ordinal);

    private readonly Lazy<string?> _executable = new(() => ExecutableLocator.FindInPath("git"));

    /// <summary>Reads the repository's state as it is locally (no network): fast enough to show straight away.</summary>
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

    /// <summary>
    /// Fetches the branch's remote, then returns <paramref name="info"/> with up-to-date <see cref="GitInfo.Ahead"/> and
    /// <see cref="GitInfo.Behind"/> counts (later <see cref="GetInfoAsync"/> calls return it too). Slow: run it in the
    /// background. A failed or timed out fetch gives the counts as of the last fetch.
    /// </summary>
    public async Task<GitInfo> FetchAsync(GitInfo info, CancellationToken cancellationToken = default)
    {
        if (_executable.Value is not { } git)
        {
            return info;
        }

        try
        {
            await FetchRemoteAsync(git, info.RepositoryRoot, cancellationToken);
            var (upstream, ahead, behind) = await ReadUpstreamAsync(git, info.RepositoryRoot, cancellationToken);
            var updated = info with { Upstream = upstream, Ahead = ahead, Behind = behind };
            _byRepository[info.RepositoryRoot] = Task.FromResult<GitInfo?>(updated);
            return updated;
        }
        catch (ProcessStartException)
        {
            return info;
        }
    }

    private async Task<(string? Upstream, int Ahead, int Behind)> ReadUpstreamAsync(string git, string repositoryRoot, CancellationToken cancellationToken)
    {
        var upstream = RunAsync(git, repositoryRoot, ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"], cancellationToken);
        var counts = RunAsync(git, repositoryRoot, ["rev-list", "--left-right", "--count", "HEAD...@{upstream}"], cancellationToken);
        var upstreamName = (await upstream)?.Trim();
        var (ahead, behind) = ParseCounts(await counts);
        return (string.IsNullOrEmpty(upstreamName) ? null : upstreamName, ahead, behind);
    }

    private async Task<GitInfo?> ReadAsync(string git, string repositoryRoot, CancellationToken cancellationToken)
    {
        var upstream = ReadUpstreamAsync(git, repositoryRoot, cancellationToken);
        var branch = RunAsync(git, repositoryRoot, ["symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken);
        var log = RunAsync(git, repositoryRoot, ["log", "-1", "--format=%H%x1f%h%x1f%cI%x1f%an%x1f%s"], cancellationToken);
        var status = RunAsync(git, repositoryRoot, ["status", "--porcelain"], cancellationToken);
        await Task.WhenAll(branch, log, status, upstream);

        // No commits yet (or a broken repository): nothing worth showing.
        if (await log is not { } logOutput || ParseLog(logOutput) is not { } last)
        {
            return null;
        }

        var branchName = (await branch)?.Trim();
        var (upstreamName, ahead, behind) = await upstream;
        return new GitInfo(
            repositoryRoot,
            string.IsNullOrEmpty(branchName) ? null : branchName,
            last.Commit,
            last.ShortCommit,
            last.Subject,
            last.Author,
            last.Date,
            HasChanges: !string.IsNullOrWhiteSpace(await status),
            Upstream: upstreamName,
            Ahead: ahead,
            Behind: behind);
    }

    /// <summary>Fetches the current branch's remote, quietly; a failure or timeout just leaves the old counts.</summary>
    private async Task FetchRemoteAsync(string git, string repositoryRoot, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FetchTimeout);
        try
        {
            await RunAsync(git, repositoryRoot, ["fetch", "--quiet", "--no-tags", "--no-recurse-submodules"], timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Too slow (or waiting on something): not worth holding up the wizard.
        }
    }

    /// <summary>Parses <c>git rev-list --left-right --count HEAD...@{upstream}</c>: "ahead\tbehind".</summary>
    internal static (int Ahead, int Behind) ParseCounts(string? output)
    {
        var parts = output?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return parts.Length == 2 && int.TryParse(parts[0], CultureInfo.InvariantCulture, out var ahead) && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var behind)
            ? (ahead, behind)
            : (0, 0);
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
            // GCM_INTERACTIVE stops Git Credential Manager from opening a sign-in window during the fetch.
            Environment = new Dictionary<string, string?>
            {
                ["GIT_TERMINAL_PROMPT"] = "0", ["GCM_INTERACTIVE"] = "never", ["GIT_PAGER"] = "cat", ["GIT_OPTIONAL_LOCKS"] = "0"
            }
        };

        var result = await processRunner.RunAsync(spec, cancellationToken: cancellationToken);
        return result.Succeeded ? result.Output : null;
    }
}
