using Pubwiz.Core.Infrastructure.Secrets;
using Pubwiz.WebDeploy;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pubwiz.Core.Targets.WebDeploy;

/// <summary>A Web Deploy server with saved sign-in details.</summary>
/// <param name="Server">The server's host as written in profiles, e.g. <c>10.0.0.5</c> or <c>iis.contoso.com</c> (lower case).</param>
/// <param name="UserName">The user to sign in as.</param>
public sealed record ServerCredential(string Server, string UserName);

/// <summary>
/// User names and passwords saved per Web Deploy server. When a profile deploys to a saved server, these are used
/// instead of the profile's user name and password, so many profiles (and machines that check them out of git) can
/// share one login without each storing it.
/// </summary>
/// <remarks>
/// The OS credential store can't list what it holds, so the servers and user names are kept in a small file in the
/// user's config folder (<c>~/.config/pubwiz/servers.json</c>, <c>%APPDATA%\pubwiz\servers.json</c>), and only the
/// passwords go in the credential store. Servers are matched by host as written in the profile's
/// <c>MSDeployServiceURL</c> (an IP address or a name; names aren't resolved), whatever the port.
/// </remarks>
/// <param name="secrets">Holds the passwords.</param>
/// <param name="filePath">The servers file; <see cref="DefaultFilePath"/> outside tests.</param>
public sealed class ServerCredentialStore(ISecretStore secrets, string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>The servers file in the user's config folder.</summary>
    public static string DefaultFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pubwiz", "servers.json");

    public string FilePath { get; } = filePath;

    /// <summary>Where the passwords are kept, for messages.</summary>
    public string SecretStoreName => secrets.Name;

    /// <summary>The saved servers, sorted.</summary>
    public IReadOnlyList<ServerCredential> List() =>
        Load().Servers.Select(s => new ServerCredential(s.Key, s.Value.UserName)).OrderBy(s => s.Server, StringComparer.Ordinal).ToList();

    /// <summary>The saved login for a server, or <c>null</c> when there's none.</summary>
    /// <param name="server">A host, <c>host:port</c> or URL; see <see cref="ToServerKey"/>.</param>
    public ServerCredential? Find(string server)
    {
        var key = ToServerKey(server);
        return Load().Servers.TryGetValue(key, out var entry) ? new ServerCredential(key, entry.UserName) : null;
    }

    /// <summary>The saved password of a server, or <c>null</c> when it's missing from the credential store.</summary>
    /// <exception cref="SecretStoreException">The credential store couldn't be read.</exception>
    public Task<string?> GetPasswordAsync(ServerCredential server, CancellationToken cancellationToken) =>
        secrets.GetAsync(SecretKeys.ForServer(server.Server), cancellationToken);

    /// <summary>Saves (or replaces) a server's login.</summary>
    /// <returns>The saved entry, with the server written the way it's matched.</returns>
    /// <exception cref="ArgumentException"><paramref name="server"/> isn't a host, <c>host:port</c> or URL.</exception>
    /// <exception cref="SecretStoreException">The password couldn't be saved.</exception>
    public async Task<ServerCredential> SaveAsync(string server, string userName, string password, CancellationToken cancellationToken)
    {
        if (!secrets.IsAvailable)
        {
            throw new SecretStoreException($"{secrets.Name} isn't available on this machine, so the password can't be saved.");
        }

        var saved = new ServerCredential(ToServerKey(server), userName);

        // The password first: a server without one in the file would fail every publish to it.
        await secrets.SetAsync(SecretKeys.ForServer(saved.Server), SecretKeys.LabelForServer(saved.Server, userName), password, cancellationToken);

        var file = Load();
        file.Servers[saved.Server] = new ServerEntry(userName);
        Save(file);
        return saved;
    }

    /// <summary>Forgets a server's login.</summary>
    /// <returns><c>false</c> when the server wasn't saved.</returns>
    /// <exception cref="ArgumentException"><paramref name="server"/> isn't a host, <c>host:port</c> or URL.</exception>
    public async Task<bool> RemoveAsync(string server, CancellationToken cancellationToken)
    {
        var key = ToServerKey(server);
        var file = Load();
        if (!file.Servers.Remove(key))
        {
            return false;
        }

        Save(file);
        if (secrets.IsAvailable)
        {
            await secrets.DeleteAsync(SecretKeys.ForServer(key), cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// How a server is stored and matched: its host in lower case. Accepts what users type and what profiles contain,
    /// e.g. <c>10.0.0.5</c>, <c>10.0.0.5:8172</c> or <c>https://10.0.0.5:8172/msdeploy.axd</c> all give <c>10.0.0.5</c>.
    /// </summary>
    /// <exception cref="ArgumentException">The text isn't a host, <c>host:port</c> or URL.</exception>
    public static string ToServerKey(string server)
    {
        try
        {
            // The same parsing as profiles' MSDeployServiceURL, so a server matches however it was written.
            return WebDeployEndpoint.Create(server, iisPath: "site").ServiceUrl.Host.ToLowerInvariant();
        }
        catch (UriFormatException)
        {
            throw new ArgumentException($"'{server}' isn't a server name, IP address or URL.", nameof(server));
        }
    }

    private ServerFile Load()
    {
        if (!File.Exists(FilePath))
        {
            return new ServerFile();
        }

        try
        {
            var file = JsonSerializer.Deserialize<ServerFile>(File.ReadAllText(FilePath), JsonOptions) ?? new ServerFile();

            // System.Text.Json fills the dictionary without the case-insensitive comparer.
            return new ServerFile { Servers = new Dictionary<string, ServerEntry>(file.Servers, StringComparer.OrdinalIgnoreCase) };
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{FilePath} is not a valid servers file: {ex.Message}", ex);
        }
    }

    private void Save(ServerFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(file, JsonOptions) + Environment.NewLine);
    }

    /// <summary>The servers file: <c>{ "servers": { "10.0.0.5": { "userName": "deploy" } } }</c>.</summary>
    private sealed class ServerFile
    {
        [JsonPropertyName("servers")]
        public Dictionary<string, ServerEntry> Servers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ServerEntry(string UserName);
}
