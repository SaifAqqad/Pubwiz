using Pubwiz.Core.Profiles;
using Pubwiz.Core.Tests.TestHelpers;

namespace Pubwiz.Core.Tests.Profiles;

public sealed class PublishProfileStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    [Fact]
    public void SaveThenList_RoundTripsProfiles()
    {
        var profile = new PublishProfile("Production");
        profile[ProfileProperties.WebPublishMethod] = "ZipDeploy";

        var saved = PublishProfileStore.Save(_temp.Path, profile);
        var listed = PublishProfileStore.List(_temp.Path);

        Assert.Equal(Path.Combine(_temp.Path, "Properties", "PublishProfiles", "Production.pubxml"), saved.FilePath);
        Assert.Equal(["Production"], listed.Select(p => p.Name));
        Assert.Equal(PublishMethod.ZipDeploy, listed[0].Method);
    }

    [Fact]
    public void Save_RefusesToOverwriteByDefault()
    {
        var profile = new PublishProfile("Production");
        PublishProfileStore.Save(_temp.Path, profile);

        Assert.Throws<IOException>(() => PublishProfileStore.Save(_temp.Path, profile));
        PublishProfileStore.Save(_temp.Path, profile, overwrite: true);
    }

    [Fact]
    public void List_SkipsInvalidFiles()
    {
        _temp.CreateFile("Properties/PublishProfiles/Broken.pubxml", "<Project>");

        Assert.Empty(PublishProfileStore.List(_temp.Path));
    }

    [Theory]
    [InlineData("my-site - Web Deploy", "my-site - Web Deploy")]
    [InlineData("a/b:c", "a_b_c")]
    [InlineData("  ", "Profile")]
    public void ToValidName_ReplacesInvalidCharacters(string name, string expected)
    {
        Assert.Equal(expected, PublishProfileStore.ToValidName(name));
    }

    public void Dispose() => _temp.Dispose();
}
