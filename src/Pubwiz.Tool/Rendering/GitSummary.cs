using Pubwiz.Core.Infrastructure.Git;

namespace Pubwiz.Tool.Rendering;

/// <summary>
/// The one-line git summary shown while choosing what to publish, e.g.
/// <c>Git main · 2a2755a · Saif · 2 days ago (2026-10-30 14:02) · 3 commits to pull · uncommitted changes</c>.
/// </summary>
public static class GitSummary
{
    /// <summary>A piece of the line; the lowest <paramref name="Priority"/> is left out first when it doesn't fit.</summary>
    private sealed record Part(string Markup, int Priority);

    /// <param name="git">The repository state.</param>
    /// <param name="now">For "x ago".</param>
    /// <param name="maxWidth">Leave out the least important parts (exact date, then author, ...) until the line fits.</param>
    public static string ToMarkup(GitInfo git, DateTimeOffset now, int? maxWidth = null)
    {
        var branch = git.Branch is null
            ? "[yellow]detached HEAD[/]"
            : $"[{Widgets.PrimaryColorMarkup}]{git.Branch.EscapeMarkup()}[/]";

        List<Part> parts =
        [
            new($"[dim]Git[/] {branch}", 10),
            new($"[bold]{git.ShortCommit.EscapeMarkup()}[/]", 9),
            new($"[dim]{git.Author.EscapeMarkup()}[/]", 2),
            new($"[dim]{DisplayText.RelativeTime(git.CommitDate, now)}[/]", 5),
            new($"[dim]({git.CommitDate.ToLocalTime():yyyy-MM-dd HH:mm})[/]", 1)
        ];

        if (git.Behind > 0)
        {
            parts.Add(new($"[yellow]{Commits(git.Behind)} to pull[/]", 8));
        }

        if (git.Ahead > 0)
        {
            parts.Add(new($"[dim]{Commits(git.Ahead)} to push[/]", 4));
        }

        if (git.HasChanges)
        {
            parts.Add(new("[yellow]uncommitted changes[/]", 7));
        }

        while (maxWidth is { } width && parts.Count > 1 && Markup.Remove(Join(parts)).Length > width)
        {
            parts.Remove(parts.MinBy(p => p.Priority)!);
        }

        return Join(parts);
    }

    /// <summary>"·" between parts, except before the exact date, which belongs to "x ago".</summary>
    private static string Join(List<Part> parts) =>
        string.Concat(parts.Select((part, i) => i == 0 ? part.Markup : part.Markup.StartsWith("[dim](", StringComparison.Ordinal) ? " " + part.Markup : " [dim]·[/] " + part.Markup));

    private static string Commits(int count) => count == 1 ? "1 commit" : $"{count} commits";
}
