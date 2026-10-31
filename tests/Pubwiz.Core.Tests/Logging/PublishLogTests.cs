using Pubwiz.Core.Logging;

namespace Pubwiz.Core.Tests.Logging;

public class PublishLogTests
{
    [Fact]
    public void Tail_ReturnsTheLastLines()
    {
        var log = new PublishLog();
        for (var i = 1; i <= 20; i++)
        {
            log.Info($"line {i}");
        }

        Assert.Equal(["line 19", "line 20"], log.Tail(2).Select(e => e.Text));
        Assert.Equal(20, log.Count);
    }

    [Fact]
    public void Write_TagsLinesWithTheCurrentStep()
    {
        var log = new PublishLog { CurrentStep = "Upload" };

        log.Write(LogSource.Output, "sent 3 files");

        Assert.Equal("Upload", log.Snapshot().Single().Step);
    }

    [Fact]
    public void AddSecret_IgnoresVeryShortValues()
    {
        var log = new PublishLog();
        log.AddSecret("ab");

        log.Info("about");

        Assert.Equal("about", log.Snapshot().Single().Text);
    }
}
