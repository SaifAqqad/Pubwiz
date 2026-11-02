using Pubwiz.Core.Infrastructure.Secrets;
using Pubwiz.Core.Targets.WebDeploy;
using Pubwiz.Core.Tests.TestHelpers;

namespace Pubwiz.Core.Tests.Targets.WebDeploy;

public sealed class ServerCredentialStoreTests : IDisposable
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly TempDirectory _temp = new();
    private readonly MemorySecretStore _secrets = new();
    private readonly ServerCredentialStore _store;

    public ServerCredentialStoreTests() => _store = new ServerCredentialStore(_secrets, Path.Combine(_temp.Path, "config", "servers.json"));

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("10.0.0.5", "10.0.0.5")]
    [InlineData("10.0.0.5:8172", "10.0.0.5")]
    [InlineData("https://10.0.0.5:8172/msdeploy.axd?site=Api", "10.0.0.5")]
    [InlineData(" IIS.Contoso.com ", "iis.contoso.com")]
    public void ToServerKey_IsTheHostInLowerCase(string server, string expected)
    {
        Assert.Equal(expected, ServerCredentialStore.ToServerKey(server));
    }

    [Fact]
    public async Task SaveAsync_KeepsTheUserInTheFileAndThePasswordInTheCredentialStore()
    {
        var saved = await _store.SaveAsync("https://10.0.0.5:8172/msdeploy.axd", "deploy", "p@ss", Token);

        Assert.Equal(new ServerCredential("10.0.0.5", "deploy"), saved);
        Assert.Equal("p@ss", _secrets.Secrets[SecretKeys.ForServer("10.0.0.5")]);
        Assert.DoesNotContain("p@ss", File.ReadAllText(_store.FilePath));
        Assert.Equal([saved], _store.List());
    }

    [Fact]
    public async Task Find_MatchesTheServerHoweverItIsWritten()
    {
        await _store.SaveAsync("10.0.0.5", "deploy", "p@ss", Token);

        Assert.Equal("deploy", _store.Find("10.0.0.5:8172")?.UserName);
        Assert.Null(_store.Find("10.0.0.6"));
        Assert.Equal("p@ss", await _store.GetPasswordAsync(_store.Find("10.0.0.5")!, Token));
    }

    [Fact]
    public async Task SaveAsync_ReplacesAnExistingLogin()
    {
        await _store.SaveAsync("10.0.0.5", "old", "1", Token);

        await _store.SaveAsync("10.0.0.5", "new", "2", Token);

        Assert.Equal("new", Assert.Single(_store.List()).UserName);
        Assert.Equal("2", _secrets.Secrets[SecretKeys.ForServer("10.0.0.5")]);
    }

    [Fact]
    public async Task RemoveAsync_ForgetsTheUserAndThePassword()
    {
        await _store.SaveAsync("10.0.0.5", "deploy", "p@ss", Token);

        Assert.True(await _store.RemoveAsync("10.0.0.5", Token));
        Assert.False(await _store.RemoveAsync("10.0.0.5", Token));

        Assert.Empty(_store.List());
        Assert.Empty(_secrets.Secrets);
    }

    [Fact]
    public async Task SaveAsync_FailsWithoutACredentialStore()
    {
        var store = new ServerCredentialStore(new MemorySecretStore { IsAvailable = false }, _store.FilePath);

        await Assert.ThrowsAsync<SecretStoreException>(() => store.SaveAsync("10.0.0.5", "deploy", "p@ss", Token));
        Assert.False(File.Exists(_store.FilePath));
    }
}
