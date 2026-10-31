using Pubwiz.Core;
using Pubwiz.Core.Azure;
using Pubwiz.Core.Platform;
using Pubwiz.Core.Processes;

namespace Pubwiz.Tool.Azure;

/// <summary>
/// Gets the Azure CLI ready: installed (offering the right install command), signed in, and on the right subscription.
/// </summary>
public sealed class AzureSession(IAnsiConsole console, AzCli az, IProcessRunner processRunner)
{
    private AzureSubscription? _subscription;

    /// <summary>
    /// Makes sure az is installed and signed in, and lets the user pick a subscription (once per run).
    /// </summary>
    public async Task<AzureSubscription> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (_subscription is not null)
        {
            return _subscription;
        }

        await EnsureInstalledAsync(cancellationToken);

        var account = await az.GetCurrentAccountAsync(cancellationToken) ?? await SignInAsync(cancellationToken);
        return _subscription = await ChooseSubscriptionAsync(account, cancellationToken);
    }

    private async Task EnsureInstalledAsync(CancellationToken cancellationToken)
    {
        if (az.IsInstalled)
        {
            return;
        }

        console.MarkupLine("The Azure CLI ([bold]az[/]) is needed to find your apps and sign in.");
        if (AzCliInstallGuide.GetRecommended() is not { } option)
        {
            throw new PrerequisiteMissingException("Azure (az)", $"Install it ({AzCliInstallGuide.DocsUrl}), then run this again.");
        }

        console.MarkupLine($"{option.Description.EscapeMarkup()}:");
        console.MarkupLine($"  [{Widgets.PrimaryColorMarkup}]{option.Command.EscapeMarkup()}[/]");
        if (!console.Confirm("Run this command now?", defaultValue: false))
        {
            throw new PrerequisiteMissingException("Azure (az)", $"Install it ({AzCliInstallGuide.DocsUrl}), then run this again.");
        }

        var exitCode = await Shell.RunInteractiveAsync(processRunner, option.Command, cancellationToken);
        if (exitCode != 0 || !az.Refresh())
        {
            throw new PrerequisiteMissingException("Azure (az)", $"The install command didn't finish (exit code {exitCode}). See {AzCliInstallGuide.DocsUrl}.");
        }

        console.MarkupLine($"{Widgets.SuccessCheckMarkup} Azure CLI installed");
    }

    private async Task<AzureSubscription> SignInAsync(CancellationToken cancellationToken)
    {
        const string browser = "Sign in with a browser";
        const string deviceCode = "Sign in with a code on another device";

        var choices = HasDesktop() ? new[] { browser, deviceCode } : [deviceCode, browser];
        var useDeviceCode = console.Prompt(new SelectionPrompt<string>()
            .Title("You're not signed in to Azure.")
            .HighlightStyle(new Style(Widgets.PrimaryColor))
            .AddChoices(choices)) == deviceCode;

        await az.LoginAsync(useDeviceCode, tenant: null, cancellationToken);
        return await az.GetCurrentAccountAsync(cancellationToken)
            ?? throw new UserErrorException("Signing in to Azure didn't complete.");
    }

    private async Task<AzureSubscription> ChooseSubscriptionAsync(AzureSubscription current, CancellationToken cancellationToken)
    {
        var subscriptions = await console.Status().StartAsync("Loading subscriptions...", _ => az.ListSubscriptionsAsync(cancellationToken));
        if (subscriptions.Count <= 1)
        {
            WriteSignedIn(current);
            return current;
        }

        // The active subscription comes first, so Enter keeps it.
        var ordered = subscriptions.OrderByDescending(s => s.Id == current.Id).ThenBy(s => s.Name).ToList();
        var chosen = console.Prompt(new SelectionPrompt<AzureSubscription>()
            .Title("Azure subscription:")
            .HighlightStyle(new Style(Widgets.PrimaryColor))
            .AddChoices(ordered)
            .UseConverter(s => $"{s.Name.EscapeMarkup()} [dim]{s.Id}[/]"));

        if (chosen.Id != current.Id)
        {
            await az.SetSubscriptionAsync(chosen.Id, cancellationToken);
        }

        WriteSignedIn(chosen);
        return chosen;
    }

    private void WriteSignedIn(AzureSubscription subscription)
    {
        var user = subscription.User?.Name ?? "unknown user";
        console.MarkupLine($"[dim]Azure: {user.EscapeMarkup()} · {subscription.Name.EscapeMarkup()}[/]");
    }

    /// <summary>Browser sign-in needs a desktop session on this machine (not SSH).</summary>
    private static bool HasDesktop()
    {
        if (Environment.GetEnvironmentVariable("SSH_CONNECTION") is not null)
        {
            return false;
        }

        return !OperatingSystemInfo.IsLinux
               || Environment.GetEnvironmentVariable("DISPLAY") is not null
               || Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is not null;
    }
}
