using Pubwiz.Core.Discovery;
using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Infrastructure.Secrets;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets.Azure;
using Pubwiz.Tool.Credentials;
using Spectre.Console.Testing;

namespace Pubwiz.Tool.Tests.Credentials;

public class PasswordProviderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly ProjectInfo Project = new() { Name = "Api", FilePath = Path.Combine(Path.GetTempPath(), "Api", "Api.csproj"), Kind = ProjectKind.Web };

    private readonly TestConsole _console = new TestConsole().Interactive();
    private readonly MemorySecretStore _store = new();
    private readonly PublishProfile _profile;
    private readonly string _key;

    public PasswordProviderTests()
    {
        _profile = new PublishProfile("Iis", Path.Combine(Project.Directory, "Properties", "PublishProfiles", "Iis.pubxml"));
        _profile[ProfileProperties.WebPublishMethod] = "MSDeploy";
        _profile[ProfileProperties.UserName] = "deploy";
        _key = SecretKeys.ForProfile(_profile);
        _store.Secrets[_key] = "old";
    }

    private PasswordProvider CreateProvider() => new(_console, _store, new AzurePublishingCredentials(new AzCli(new ProcessRunner())));

    [Fact]
    public async Task AsksBeforeUsingASavedPassword()
    {
        _console.Input.PushKey(ConsoleKey.Enter); // "Yes"

        var password = await CreateProvider().GetAsync(Project, _profile, PasswordPrompt.Always, Token);

        Assert.Equal("old", password);
        Assert.Contains("Use the saved password for Api / Iis (deploy)?", _console.Output);
        Assert.Equal("old", _store.Secrets[_key]);
    }

    [Fact]
    public async Task CanRemoveTheSavedPasswordAndEnterAnother()
    {
        _console.Input.PushKey(ConsoleKey.DownArrow); // "No, remove it ..."
        _console.Input.PushKey(ConsoleKey.Enter);
        _console.Input.PushTextWithEnter("new");      // the password
        _console.Input.PushTextWithEnter("n");        // don't save it

        var password = await CreateProvider().GetAsync(Project, _profile, PasswordPrompt.Always, Token);

        Assert.Equal("new", password);
        Assert.False(_store.Secrets.ContainsKey(_key));
    }

    [Fact]
    public async Task UsesTheSavedPasswordWithoutAskingWhenOnlyMissingOnesAreAsked()
    {
        var password = await CreateProvider().GetAsync(Project, _profile, PasswordPrompt.WhenMissing, Token);

        Assert.Equal("old", password);
        Assert.DoesNotContain("saved password", _console.Output);
    }

    [Fact]
    public async Task AsksOnlyOncePerRun()
    {
        var provider = CreateProvider();
        _console.Input.PushKey(ConsoleKey.Enter);
        await provider.GetAsync(Project, _profile, PasswordPrompt.Always, Token);

        // Publishing after the wizard asks again with the same settings; the answer is remembered.
        var password = await provider.GetAsync(Project, _profile, PasswordPrompt.Always, Token);

        Assert.Equal("old", password);
    }

    /// <summary>An in-memory credential store, so tests never touch the real OS keychain.</summary>
    private sealed class MemorySecretStore : ISecretStore
    {
        public Dictionary<string, string> Secrets { get; } = new(StringComparer.Ordinal);

        public string Name => "Test store";

        public bool IsAvailable => true;

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(Secrets.GetValueOrDefault(key));

        public Task SetAsync(string key, string label, string secret, CancellationToken cancellationToken = default)
        {
            Secrets[key] = secret;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            Secrets.Remove(key);
            return Task.CompletedTask;
        }
    }
}
