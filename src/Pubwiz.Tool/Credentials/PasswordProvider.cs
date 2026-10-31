using Pubwiz.Core.Discovery;
using Pubwiz.Core.Infrastructure.Secrets;
using Pubwiz.Core.Profiles;

namespace Pubwiz.Tool.Credentials;

/// <summary>
/// Gets deployment passwords, in this order: an environment variable (for CI), the OS secret store,
/// then asking (and offering to save the answer).
/// </summary>
/// <remarks>
/// A broken secret store (no keyring running, access denied, ...) never stops publishing:
/// it's reported as a warning and the password is asked for instead.
/// </remarks>
public sealed class PasswordProvider(IAnsiConsole console, ISecretStore store)
{
    /// <summary>Where passwords are saved, for messages.</summary>
    public string StoreName => store.Name;

    /// <summary>Used for every profile when no profile-specific variable is set.</summary>
    public const string PasswordVariable = "PUBWIZ_PASSWORD";

    /// <param name="project">The project being published.</param>
    /// <param name="profile">The saved profile.</param>
    /// <param name="allowPrompt"><c>false</c> in non-interactive runs: a missing password is then an error.</param>
    /// <param name="cancellationToken">Cancels the secret store lookup.</param>
    /// <exception cref="UserErrorException">No password was found and prompting isn't allowed.</exception>
    public async Task<string> GetAsync(ProjectInfo project, PublishProfile profile, bool allowPrompt, CancellationToken cancellationToken)
    {
        if (ReadEnvironment(profile) is { } fromEnvironment)
        {
            return fromEnvironment;
        }

        if (await TryReadAsync(SecretKeys.ForProfile(profile), cancellationToken) is { } saved)
        {
            return saved;
        }

        if (!allowPrompt)
        {
            throw new UserErrorException($"No password for '{profile.Name}' ({project.Name}). Set {ProfileVariable(profile)} or {PasswordVariable}, or save it in {store.Name} by publishing once interactively.");
        }

        var userName = profile[ProfileProperties.UserName];
        var password = userName is null
            ? console.AskSecret($"Password for {profile.Name}")
            : console.AskSecret($"Password for {userName}", hint: profile.Name);

        if (store.IsAvailable && console.AskYesNo($"Save it in {store.Name}?", defaultValue: true))
        {
            await TrySaveAsync(project, profile, password, cancellationToken);
        }

        return password;
    }

    /// <summary>Saves a password that came with an imported profile.</summary>
    /// <returns><c>true</c> when it was saved.</returns>
    public async Task<bool> TrySaveAsync(ProjectInfo project, PublishProfile profile, string password, CancellationToken cancellationToken)
    {
        if (!store.IsAvailable)
        {
            return false;
        }

        try
        {
            await store.SetAsync(SecretKeys.ForProfile(profile), SecretKeys.LabelForProfile(project.Name, profile), password, cancellationToken);
            return true;
        }
        catch (SecretStoreException ex)
        {
            console.Write(Widgets.Warning($"Could not save the password: {ex.Message}"));
            console.WriteLine();
            return false;
        }
    }

    /// <summary>
    /// The profile-specific variable: <c>PUBWIZ_PASSWORD_</c> + the profile name in upper case, with anything
    /// that isn't a letter or digit as <c>_</c> (e.g. "contoso - Web Deploy" → PUBWIZ_PASSWORD_CONTOSO_WEB_DEPLOY).
    /// </summary>
    public static string ProfileVariable(PublishProfile profile)
    {
        var name = new string(profile.Name.ToUpperInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());
        while (name.Contains("__", StringComparison.Ordinal))
        {
            name = name.Replace("__", "_", StringComparison.Ordinal);
        }

        return $"{PasswordVariable}_{name.Trim('_')}";
    }

    private static string? ReadEnvironment(PublishProfile profile)
    {
        var value = Environment.GetEnvironmentVariable(ProfileVariable(profile)) ?? Environment.GetEnvironmentVariable(PasswordVariable);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private async Task<string?> TryReadAsync(string key, CancellationToken cancellationToken)
    {
        if (!store.IsAvailable)
        {
            return null;
        }

        try
        {
            return await store.GetAsync(key, cancellationToken);
        }
        catch (SecretStoreException ex)
        {
            console.Write(Widgets.Warning($"Could not read from {store.Name}: {ex.Message}"));
            console.WriteLine();
            return null;
        }
    }
}
