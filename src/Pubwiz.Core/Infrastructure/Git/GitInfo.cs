namespace Pubwiz.Core.Infrastructure.Git;

/// <summary>
/// The state of the git repository a project lives in, shown before publishing and written to reports.
/// </summary>
/// <param name="RepositoryRoot">The repository's top folder.</param>
/// <param name="Branch">The checked-out branch, or <c>null</c> when HEAD is detached.</param>
/// <param name="Commit">The full hash of the last commit.</param>
/// <param name="ShortCommit">The abbreviated hash, e.g. <c>2a2755a</c>.</param>
/// <param name="Subject">The first line of the last commit's message.</param>
/// <param name="Author">Who wrote the last commit.</param>
/// <param name="CommitDate">When the last commit was made.</param>
/// <param name="HasChanges"><c>true</c> when there are uncommitted changes (including untracked files).</param>
public sealed record GitInfo(
    string RepositoryRoot,
    string? Branch,
    string Commit,
    string ShortCommit,
    string Subject,
    string Author,
    DateTimeOffset CommitDate,
    bool HasChanges);
