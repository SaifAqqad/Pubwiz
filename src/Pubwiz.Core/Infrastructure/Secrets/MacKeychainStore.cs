using Pubwiz.Core.Infrastructure.Processes;

namespace Pubwiz.Core.Infrastructure.Secrets;

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
        // -g writes the password to stderr; the command fails when the item doesn't exist.
        // (-w prints only the password, but as bare hex when it has any non-ASCII character, which
        // can't be told apart from a password that is itself hex.)
        var result = await RunAsync(["find-generic-password", "-s", SecretKeys.Service, "-a", key, "-g"], cancellationToken);
        return result.Succeeded ? ParsePasswordLine(result.Error) : null;
    }

    /// <summary>
    /// Reads the <c>password:</c> line of <c>security find-generic-password -g</c>:
    /// <c>password: "secret"</c> for printable ASCII (quotes inside aren't escaped),
    /// <c>password: 0x70C3B6...  "p\303\266..."</c> (UTF-8 bytes in hex) for anything else, or <c>password: </c> when empty.
    /// </summary>
    internal static string? ParsePasswordLine(string output)
    {
        const string prefix = "password:";
        var line = output.Split('\n').Select(l => l.TrimEnd('\r')).FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
        if (line is null)
        {
            return null;
        }

        var value = line[prefix.Length..].Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var end = value.IndexOf(' ');
            var hex = end < 0 ? value[2..] : value[2..end];
            return System.Text.Encoding.UTF8.GetString(Convert.FromHexString(hex));
        }

        return value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
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
