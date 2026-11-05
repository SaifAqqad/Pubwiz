using Pubwiz.Core.Discovery;
using Pubwiz.Core.Infrastructure.Secrets;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets.Azure;

namespace Pubwiz.Tool.Credentials;

/// <summary>When <see cref="PasswordProvider"/> may ask the user.</summary>
public enum PasswordPrompt
{
    /// <summary>Never (no terminal, CI): a missing password is an error.</summary>
    Never,

    /// <summary>Only when no password was found (e.g. with <c>--yes</c>).</summary>
    WhenMissing,

    /// <summary>Also when one is saved: use it, or remove it and enter another.</summary>
    Always
}

/// <summary>
/// Gets deployment passwords, in this order: an environment variable (for CI), Azure (for App Service profiles,
/// through the signed-in az), the OS secret store, then asking (and offering to save the answer).
/// </summary>
/// <remarks>
/// An answer is remembered for the rest of the run, so the wizard can ask before it hands over the batch
/// (where Esc goes back to the project list) without being asked again when publishing.
/// Esc in the password prompts throws <see cref="PromptCancelledException"/>.
/// <para>
/// A saved password can go stale (changed on the server), so interactive runs ask whether to use it; the user can
/// remove it from the secret store there and type the new one.
/// </para>
/// <para>
/// Azure comes before the secret store because it's always current: a saved password goes stale when the app's
/// publishing credentials are reset.
/// </para>
/// <para>
/// A broken secret store (no keyring running, access denied, ...) never stops publishing:
/// it's reported as a warning and the password is asked for instead.
/// </para>
/// <para>
/// An App Service app with basic authentication turned off gets no password at all (<c>null</c>): no password works
/// there, so publishing signs in with az instead.
/// </para>
/// </remarks>
public sealed class PasswordProvider(IAnsiConsole console, ISecretStore store, AzurePublishingCredentials azure)
{
    /// <summary>Where passwords are saved, for messages.</summary>
    public string StoreName => store.Name;

    /// <summary>Used for every profile when no profile-specific variable is set.</summary>
    public const string PasswordVariable = "PUBWIZ_PASSWORD";

    /// <summary>The answers so far; <c>null</c> for apps that sign in with az instead.</summary>
    private readonly Dictionary<string, string?> _asked = new(StringComparer.Ordinal);

    /// <param name="project">The project being published.</param>
    /// <param name="profile">The saved profile.</param>
    /// <param name="prompt">When the user may be asked.</param>
    /// <param name="cancellationToken">Cancels the secret store lookup.</param>
    /// <returns>The password, or <c>null</c> when the App Service app signs in with az instead.</returns>
    /// <exception cref="UserErrorException">No password was found and prompting isn't allowed.</exception>
    public async Task<string?> GetAsync(ProjectInfo project, PublishProfile profile, PasswordPrompt prompt, CancellationToken cancellationToken)
    {
        if (ReadEnvironment(profile) is { } fromEnvironment)
        {
            return fromEnvironment;
        }

        var key = SecretKeys.ForProfile(profile);
        if (_asked.TryGetValue(key, out var asked))
        {
            return asked;
        }

        switch (await TryFetchFromAzureAsync(profile, cancellationToken))
        {
            case AzureCredentialsResult.Found found:
                _asked[key] = found.Password;
                return found.Password;

            case AzureCredentialsResult.UseAzSignIn:
                _asked[key] = null;
                return null;
        }

        if (await TryReadAsync(key, cancellationToken) is { } saved)
        {
            if (prompt != PasswordPrompt.Always || AskToUseSaved(project, profile))
            {
                _asked[key] = saved;
                return saved;
            }

            await TryForgetAsync(key, profile);
        }

        if (prompt == PasswordPrompt.Never)
        {
            throw new UserErrorException($"No password for '{profile.Name}' ({project.Name}). Set {ProfileVariable(profile)} or {PasswordVariable}, or save it in {store.Name} by publishing once interactively.");
        }

        return await AskAsync(project, profile, key, cancellationToken);
    }

    /// <summary>Asks for the password, offers to save it, and remembers it for the run.</summary>
    private async Task<string> AskAsync(ProjectInfo project, PublishProfile profile, string key, CancellationToken cancellationToken)
    {
        var userName = profile[ProfileProperties.UserName];
        var isStaticWebApp = string.Equals(profile[ProfileProperties.PublishProvider], ProfileProperties.AzureStaticWebAppProvider, StringComparison.OrdinalIgnoreCase);
        var password = isStaticWebApp ? console.AskSecret($"Deployment token for {profile.Name}", hint: "Azure portal: the Static Web App's Overview, Manage deployment token")
            : userName is null ? console.AskSecret($"Password for {profile.Name}")
            : console.AskSecret($"Password for {userName}", hint: profile.Name);

        if (store.IsAvailable && console.AskYesNo($"Save it in {store.Name}?", defaultValue: true))
        {
            await TrySaveAsync(project, profile, password, cancellationToken);
        }

        _asked[key] = password;
        return password;
    }

    /// <summary>
    /// Asks whether to publish with the saved password, or remove it (e.g. it was changed on the server) and type another.
    /// </summary>
    /// <returns><c>true</c> to use the saved password.</returns>
    /// <exception cref="PromptCancelledException">Esc was pressed.</exception>
    private bool AskToUseSaved(ProjectInfo project, PublishProfile profile)
    {
        var user = profile[ProfileProperties.UserName] is { } userName ? $" ({userName})" : string.Empty;
        var choice = console.Choose($"Use the saved password for {project.Name} / {profile.Name}{user}?",
            [SavedPasswordChoice.Use, SavedPasswordChoice.RemoveAndEnterAnother], c => c == SavedPasswordChoice.Use
                ? "Yes"
                : $"No, remove it from {store.Name.EscapeMarkup()} and enter another");

        return choice == SavedPasswordChoice.Use;
    }

    private enum SavedPasswordChoice
    {
        Use,
        RemoveAndEnterAnother
    }

    /// <summary>Removes a saved password. A failure is only a warning: the user is asked for the password anyway.</summary>
    private async Task TryForgetAsync(string key, PublishProfile profile)
    {
        try
        {
            // Not cancellable: once the user chose to remove it, a half-done removal would only confuse.
            await store.DeleteAsync(key, CancellationToken.None);
            console.MarkupLine($"[dim]Removed the saved password for {profile.Name.EscapeMarkup()}[/]");
        }
        catch (SecretStoreException ex)
        {
            console.Write(Widgets.Warning($"Could not remove the saved password: {ex.Message}"));
            console.WriteLine();
        }
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

    /// <returns>What Azure said (also written to the console), or <c>null</c> for profiles that aren't App Service ones.</returns>
    private async Task<AzureCredentialsResult?> TryFetchFromAzureAsync(PublishProfile profile, CancellationToken cancellationToken)
    {
        if (!AzurePublishingCredentials.Applies(profile))
        {
            return null;
        }

        var fetched = await console.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(new Style(Widgets.PrimaryColor))
            .StartAsync($"Getting the publishing credentials of {profile.Name.EscapeMarkup()} from Azure...", _ => azure.TryGetAsync(profile, cancellationToken));

        switch (fetched)
        {
            case AzureCredentialsResult.Found found:
                console.MarkupLine($"[dim]Using the publishing credentials of {found.AppName.EscapeMarkup()} from Azure for {profile.Name.EscapeMarkup()}[/]");
                break;

            case AzureCredentialsResult.UseAzSignIn useAz:
                console.MarkupLine($"[dim]{useAz.AppName.EscapeMarkup()} has basic authentication turned off, so {profile.Name.EscapeMarkup()} signs in with your Azure account (az) instead of a password[/]");
                break;

            case AzureCredentialsResult.NotFound notFound:
                console.MarkupLine($"[dim]Couldn't get the password of {profile.Name.EscapeMarkup()} from Azure: {notFound.Reason.EscapeMarkup()}[/]");
                break;
        }

        return fetched;
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
