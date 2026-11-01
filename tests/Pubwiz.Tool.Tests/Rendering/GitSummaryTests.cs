using Pubwiz.Core.Infrastructure.Git;
using Pubwiz.Tool.Rendering;
using Spectre.Console;

namespace Pubwiz.Tool.Tests.Rendering;

public class GitSummaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static GitInfo Info(int ahead = 0, int behind = 0, bool hasChanges = false) =>
        new("/repo", "main", new string('a', 40), "aaaaaaa", "Secret subject", "Saif", Now.AddHours(-3), hasChanges, "origin/main", ahead, behind);

    [Fact]
    public void ShowsTheBranchCommitAndDateButNotTheSubject()
    {
        var text = Markup.Remove(GitSummary.ToMarkup(Info(), Now));

        Assert.StartsWith("Git main · aaaaaaa · Saif · 3 hours ago", text);
        Assert.DoesNotContain("Secret subject", text);
    }

    [Fact]
    public void ShowsCommitsToPullInYellow()
    {
        var markup = GitSummary.ToMarkup(Info(ahead: 1, behind: 3, hasChanges: true), Now);

        Assert.Contains("[yellow]3 commits to pull[/]", markup);
        Assert.Contains("1 commit to push", markup);
        Assert.Contains("uncommitted changes", markup);
    }

    [Fact]
    public void LeavesOutCountsWhenUpToDate()
    {
        var text = Markup.Remove(GitSummary.ToMarkup(Info(), Now));

        Assert.DoesNotContain("to pull", text);
        Assert.DoesNotContain("to push", text);
    }

    [Fact]
    public void DropsTheLeastImportantPartsToFit()
    {
        var full = Markup.Remove(GitSummary.ToMarkup(Info(behind: 2, hasChanges: true), Now));
        var narrow = Markup.Remove(GitSummary.ToMarkup(Info(behind: 2, hasChanges: true), Now, maxWidth: full.Length - 5));

        Assert.True(narrow.Length <= full.Length - 5);
        Assert.DoesNotContain("(2026", narrow);
        Assert.Contains("2 commits to pull", narrow);
        Assert.Contains("uncommitted changes", narrow);
    }
}
