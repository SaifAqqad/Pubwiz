using Pubwiz.Core.Infrastructure.Secrets;

namespace Pubwiz.Core.Tests.TestHelpers;

/// <summary>An in-memory credential store, so tests never touch the real OS keychain.</summary>
public sealed class MemorySecretStore : ISecretStore
{
    public Dictionary<string, string> Secrets { get; } = new(StringComparer.Ordinal);

    public string Name => "Test store";

    public bool IsAvailable { get; init; } = true;

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Secrets.GetValueOrDefault(key));

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
