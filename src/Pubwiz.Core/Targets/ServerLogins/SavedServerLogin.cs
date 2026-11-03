using Pubwiz.Core.Infrastructure.Secrets;
using Pubwiz.Core.Publishing;

namespace Pubwiz.Core.Targets.ServerLogins;

/// <summary>
/// A login saved for the server a profile deploys to, which a publish signs in with instead of the profile's user
/// name and password. The password is read from the credential store the first time a step needs it.
/// </summary>
/// <param name="servers">Holds the saved password.</param>
/// <param name="login">The server and user name.</param>
public sealed class SavedServerLogin(ServerCredentialStore servers, ServerCredential login)
{
    private string? _password;

    public string Server => login.Server;

    public string UserName => login.UserName;

    /// <summary>The login saved for a profile's server, or <c>null</c> when there's none.</summary>
    /// <param name="servers">The saved logins.</param>
    /// <param name="server">The server's host or URL, as written in the profile.</param>
    public static SavedServerLogin? Find(ServerCredentialStore servers, string server) =>
        servers.Find(server) is { } login ? new SavedServerLogin(servers, login) : null;

    /// <summary>The saved password; the first call logs which login is used.</summary>
    /// <exception cref="PublishStepException">The password couldn't be read, or is missing from the credential store.</exception>
    public async Task<string> GetPasswordAsync(PublishContext context, CancellationToken cancellationToken)
    {
        if (_password is not null)
        {
            return _password;
        }

        string? password;
        try
        {
            password = await servers.GetPasswordAsync(login, cancellationToken);
        }
        catch (SecretStoreException ex)
        {
            throw new PublishStepException($"The saved password for {Server} couldn't be read from {servers.SecretStoreName}: {ex.Message}", ex);
        }

        if (password is null)
        {
            throw new PublishStepException(
                $"The password for {Server} is missing from {servers.SecretStoreName}. Save it again with: dotnet pubwiz servers set {Server}");
        }

        context.Log.Info($"Signing in as {UserName}, the login saved for {Server} (instead of the profile's)");
        return _password = password;
    }
}
