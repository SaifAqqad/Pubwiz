namespace Pubwiz.Core.Secrets;

/// <summary>
/// Stores passwords in the operating system's credential store, never in project files.
/// </summary>
public interface ISecretStore
{
    /// <summary>A name for messages, e.g. "Windows Credential Manager".</summary>
    string Name { get; }

    /// <summary><c>false</c> when the store can't be used on this machine (missing tool, no keyring running, ...).</summary>
    bool IsAvailable { get; }

    /// <returns>The secret, or <c>null</c> when nothing is stored under <paramref name="key"/>.</returns>
    /// <exception cref="SecretStoreException">The store couldn't be accessed.</exception>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Adds or replaces a secret.</summary>
    /// <param name="key">Identifies the secret; see <see cref="SecretKeys"/>.</param>
    /// <param name="label">A readable description shown in the OS's password manager.</param>
    /// <param name="secret">The value to store.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="SecretStoreException">The store couldn't be accessed.</exception>
    Task SetAsync(string key, string label, string secret, CancellationToken cancellationToken = default);

    /// <summary>Removes a secret. Does nothing when it doesn't exist.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>Thrown when the OS credential store fails.</summary>
public sealed class SecretStoreException(string message, Exception? innerException = null) : Exception(message, innerException);
