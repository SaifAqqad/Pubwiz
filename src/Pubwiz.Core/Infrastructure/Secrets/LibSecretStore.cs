using Pubwiz.Core.Infrastructure.Platform;
using Pubwiz.Core.Infrastructure.Processes;

namespace Pubwiz.Core.Infrastructure.Secrets;

/// <summary>
/// Linux: the Secret Service (GNOME Keyring, KWallet, KeePassXC, ...) through libsecret's <c>secret-tool</c>.
/// </summary>
public sealed class LibSecretStore(IProcessRunner processRunner) : ISecretStore
{
    private readonly string? _secretTool = ExecutableLocator.FindInPath("secret-tool");

    public string Name => "the system keyring (libsecret)";

    /// <summary>Needs <c>secret-tool</c> and a D-Bus session (missing in plain SSH sessions).</summary>
    public bool IsAvailable => _secretTool is not null && Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is not null;

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        // "lookup" exits with 1 and prints nothing when the secret doesn't exist.
        var result = await RunAsync(["lookup", "service", SecretKeys.Service, "key", key], stdin: null, cancellationToken);
        return result.Succeeded && result.Output.Length > 0 ? result.Output.TrimEnd('\n') : null;
    }

    public async Task SetAsync(string key, string label, string secret, CancellationToken cancellationToken = default)
    {
        // The secret goes through stdin so it never appears in the process list.
        var result = await RunAsync(["store", "--label", label, "service", SecretKeys.Service, "key", key], secret, cancellationToken);
        if (!result.Succeeded)
        {
            throw new SecretStoreException($"secret-tool failed to store the password: {result.Error.Trim()}");
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        await RunAsync(["clear", "service", SecretKeys.Service, "key", key], stdin: null, cancellationToken);
    }

    private Task<ProcessResult> RunAsync(string[] arguments, string? stdin, CancellationToken cancellationToken)
    {
        if (_secretTool is null)
        {
            throw new SecretStoreException("secret-tool is not installed (package libsecret-tools or libsecret).");
        }

        return processRunner.RunAsync(new ProcessSpec(_secretTool, arguments) { StandardInput = stdin }, cancellationToken: cancellationToken);
    }
}
