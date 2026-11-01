using Pubwiz.Core.Infrastructure.Git;

namespace Pubwiz.Tool.Rendering;

/// <summary>
/// The one-line git summary shown before choosing what to publish, e.g.
/// <c>Git main · 2a2755a Bump version to 0.2.0 · Saif · 2 days ago (2026-10-30 14:02) · uncommitted changes</c>.
/// </summary>
public static class GitSummary
{
    private const int MaxSubjectLength = 60;

    public static string ToMarkup(GitInfo git, DateTimeOffset now)
    {
        var branch = git.Branch is null
            ? "[yellow]detached HEAD[/]"
            : $"[{Widgets.PrimaryColorMarkup}]{git.Branch.EscapeMarkup()}[/]";

        var date = git.CommitDate.ToLocalTime();
        var markup = $"[dim]Git[/] {branch} [dim]·[/] [bold]{git.ShortCommit.EscapeMarkup()}[/] " +
                     $"{DisplayText.Truncate(git.Subject, MaxSubjectLength).EscapeMarkup()} " +
                     $"[dim]· {git.Author.EscapeMarkup()} · {DisplayText.RelativeTime(git.CommitDate, now)} ({date:yyyy-MM-dd HH:mm})[/]";

        return git.HasChanges ? markup + " [yellow]· uncommitted changes[/]" : markup;
    }
}
