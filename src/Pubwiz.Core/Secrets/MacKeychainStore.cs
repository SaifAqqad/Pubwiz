using Pubwiz.Core.Processes;

namespace Pubwiz.Core.Secrets;

/// <summary>
/// macOS: the login keychain through the built-in <c>security</c> command.
/// </summary>
/// <remarks>
/// <c>security add-generic-password</c> only accepts the password as an argument, so it's briefly visible
/// to other processes of the same user while the command runs. Reading never exposes it.
/// </remarks>
public sealed class MacKeychainStore(IProcessRunner processRunner) : ISecretStore
{
    private const string SecurityTool = "/usr/bin/security";

    public string Name => "the macOS Keychain";

    public bool IsAvailable => File.Exists(SecurityTool);

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        // -w prints only the password; the command fails when the item doesn't exist.
        var result = await RunAsync(["find-generic-password", "-s", SecretKeys.Service, "-a", key, "-w"], cancellationToken);
        return result.Succeeded ? result.Output.TrimEnd('\n') : null;
    }

    public async Task SetAsync(string key, string label, string secret, CancellationToken cancellationToken = default)
    {
        // -U updates the item when it already exists.
        var result = await RunAsync(["add-generic-password", "-U", "-s", SecretKeys.Service, "-a", key, "-l", label, "-w", secret], cancellationToken);
        if (!result.Succeeded)
        {
            throw new SecretStoreException($"Failed to store the password in the keychain: {result.Error.Trim()}");
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        await RunAsync(["delete-generic-password", "-s", SecretKeys.Service, "-a", key], cancellationToken);
    }

    private Task<ProcessResult> RunAsync(string[] arguments, CancellationToken cancellationToken) =>
        processRunner.RunAsync(new ProcessSpec(SecurityTool, arguments), cancellationToken: cancellationToken);
}
