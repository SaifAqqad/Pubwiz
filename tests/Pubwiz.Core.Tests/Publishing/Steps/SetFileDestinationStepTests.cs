using Pubwiz.Core.Publishing.Steps;
using Pubwiz.Core.Tests.TestHelpers;

namespace Pubwiz.Core.Tests.Publishing.Steps;

public sealed class SetFileDestinationStepTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    [Fact]
    public void Resolve_KeepsAFilePath()
    {
        var zip = _temp.CreateFile("out/app.zip");

        Assert.Equal(zip, SetFileDestinationStep.Resolve(zip, [".zip"]));
    }

    [Fact]
    public void Resolve_FindsTheNewestMatchingFileInAFolder()
    {
        var old = _temp.CreateFile("images/old.tar.gz");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-1));
        var newest = _temp.CreateFile("images/app.tar.gz");
        _temp.CreateFile("images/notes.txt");

        Assert.Equal(newest, SetFileDestinationStep.Resolve(Path.Combine(_temp.Path, "images"), [".tar.gz"]));
    }

    [Fact]
    public void Resolve_KeepsAMissingPath()
    {
        var missing = Path.Combine(_temp.Path, "nowhere", "app.zip");

        Assert.Equal(missing, SetFileDestinationStep.Resolve(missing, [".zip"]));
    }

    public void Dispose() => _temp.Dispose();
}
