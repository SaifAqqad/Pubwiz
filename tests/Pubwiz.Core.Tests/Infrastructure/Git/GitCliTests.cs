using Pubwiz.Core.Infrastructure.Git;
using Pubwiz.Core.Infrastructure.Processes;

namespace Pubwiz.Core.Tests.Infrastructure.Git;

public class GitCliTests
{
    [Fact]
    public void ParseLog_ReadsTheFields()
    {
        var parsed = GitCli.ParseLog("2a2755a0c1\u001f2a2755a\u001f2026-10-30T14:02:00+03:00\u001fSaif\u001fBump version: 0.2.0\n");

        Assert.NotNull(parsed);
        Assert.Equal("2a2755a0c1", parsed.Value.Commit);
        Assert.Equal("2a2755a", parsed.Value.ShortCommit);
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 14, 2, 0, TimeSpan.FromHours(3)), parsed.Value.Date);
        Assert.Equal("Saif", parsed.Value.Author);
        Assert.Equal("Bump version: 0.2.0", parsed.Value.Subject);
    }

    [Theory]
    [InlineData("")]
    [InlineData("only\u001ftwo")]
    [InlineData("a\u001fb\u001fnot a date\u001fc\u001fd")]
    public void ParseLog_RejectsOtherOutput(string output)
    {
        Assert.Null(GitCli.ParseLog(output));
    }

    [Fact]
    public async Task GetInfoAsync_ReadsTheRepositoryOfThisCheckout()
    {
        var git = new GitCli(new ProcessRunner());

        var info = await git.GetInfoAsync(AppContext.BaseDirectory, TestContext.Current.CancellationToken);

        // Tests run from a git checkout locally; a source archive (or no git) gives null, which is fine too.
        if (info is not null)
        {
            Assert.True(Directory.Exists(info.RepositoryRoot));
            Assert.Matches("^[0-9a-f]{40}$", info.Commit);
            Assert.StartsWith(info.ShortCommit, info.Commit);
        }
    }

    [Fact]
    public async Task GetInfoAsync_IsNullOutsideARepository()
    {
        var git = new GitCli(new ProcessRunner());
        var folder = Directory.CreateTempSubdirectory("pubwiz-git-").FullName;
        try
        {
            Assert.Null(await git.GetInfoAsync(folder, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(folder);
        }
    }
}
