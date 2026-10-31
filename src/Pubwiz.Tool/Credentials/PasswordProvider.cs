using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Secrets;

namespace Pubwiz.Tool.Credentials;

/// <summary>
/// Gets deployment passwords: from the OS secret store when saved there, otherwise by asking
/// (and offering to save the answer).
/// </summary>
/// <remarks>
/// A broken secret store (no keyring running, access denied, ...) never stops publishing:
/// it's reported as a warning and the password is asked for instead.
/// </remarks>
public sealed class PasswordProvider(IAnsiConsole console, ISecretStore store)
{
    /// <summary>Where passwords are saved, for messages.</summary>
    public string StoreName => store.Name;

    public async Task<string> GetAsync(ProjectInfo project, PublishProfile profile, CancellationToken cancellationToken)
    {
        var key = SecretKeys.ForProfile(profile);
        if (await TryReadAsync(key, cancellationToken) is { } saved)
        {
            return saved;
        }

        var userName = profile[ProfileProperties.UserName];
        var question = userName is null
            ? $"Password for [{Widgets.PrimaryColorMarkup}]{profile.Name.EscapeMarkup()}[/]:"
            : $"Password for [{Widgets.PrimaryColorMarkup}]{userName.EscapeMarkup()}[/] ({profile.Name.EscapeMarkup()}):";

        var password = console.Prompt(new TextPrompt<string>(question).Secret());

        if (store.IsAvailable && console.Confirm($"Save it in {store.Name}?"))
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
