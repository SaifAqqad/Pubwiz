using Pubwiz.Core.Infrastructure.Git;

namespace Pubwiz.Tool.Rendering;

/// <summary>
/// The start folder's git state: the local read (fast, shown straight away) and, in interactive runs, the result of a
/// background fetch (commits to pull), which can take a few seconds and must never hold up the project list.
/// </summary>
/// <param name="local">The local read (<see cref="GitCli.GetInfoAsync"/>).</param>
/// <param name="fetched">The same after a fetch (<see cref="GitCli.FetchAsync"/>), or <c>null</c> when there's no fetch.</param>
public sealed class GitStatus(Task<GitInfo?> local, Task<GitInfo?>? fetched)
{
    /// <summary>The newest state available right now: the fetched one if it's done, else the local one.</summary>
    public async Task<GitInfo?> GetLatestAsync() =>
        fetched is { IsCompletedSuccessfully: true } ? fetched.Result ?? await local : await local;

    /// <summary>The fetch, while it's still running; <c>null</c> once it's done (or when there's none).</summary>
    public Task<GitInfo?>? PendingFetch => fetched is { IsCompleted: false } ? fetched : null;

    /// <summary>Starts reading <paramref name="folder"/>'s state, and fetching in the background when <paramref name="fetch"/> is set.</summary>
    public static GitStatus Start(GitCli git, string folder, bool fetch, CancellationToken cancellationToken)
    {
        var local = git.GetInfoAsync(folder, cancellationToken);
        return new GitStatus(local, fetch ? FetchAfterAsync(git, local, cancellationToken) : null);
    }

    private static async Task<GitInfo?> FetchAfterAsync(GitCli git, Task<GitInfo?> local, CancellationToken cancellationToken)
    {
        if (await local is not { } info)
        {
            return null;
        }

        try
        {
            return await git.FetchAsync(info, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return info;
        }
    }
}
