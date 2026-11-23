using Pubwiz.Core.Targets.Azure;
using Pubwiz.Tool.Azure;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Asks which deployment slot of an App Service app or Function App to publish to: the app itself (production) or
/// one of its slots, e.g. staging. Each slot is a site of its own, with its own host name and Kudu site.
/// </summary>
/// <remarks>
/// Apps without slots (and plans that don't have slots) skip the question, but the answer is still written, so it's
/// always clear where the profile publishes to. The slots are usually loaded with the apps (see <see cref="AzureAppList"/>),
/// so this doesn't wait for az.
/// </remarks>
public static class DeploymentSlotPrompt
{
    private const string Question = "Deployment slot";

    /// <returns>The chosen slot; <see cref="AzureDeploymentSlot.IsProduction"/> when it's the app itself.</returns>
    public static async Task<AzureDeploymentSlot> ChooseAsync(IAnsiConsole console, AzureAppList appList, AzureWebApp app, CancellationToken cancellationToken)
    {
        // Usually the slots came with the app; after a refresh they're asked for here.
        var slots = app.Slots ?? await console.Status().StartAsync("Loading deployment slots...", _ => appList.GetSlotsAsync(app, cancellationToken));

        var production = AzureDeploymentSlot.Production(app);
        if (slots.Count == 0)
        {
            Questions.WriteAnswer(console, Question, production.Name, note: $"{app.Name} has no other slots");
            return production;
        }

        // The same list as the apps: production first, like the Azure portal. The host name is last, since it's the
        // longest and the first to be cut on a narrow terminal. Without an exit row, the prompt always returns an item;
        // Esc goes back (PromptCancelledException).
        var prompt = new MultiSelectPrompt<AzureDeploymentSlot>($"[bold]{Question}[/]", [production, .. slots],
            slot => [slot.Name, slot.State ?? string.Empty, slot.DefaultHostName])
        {
            SingleSelect = true
        };

        var chosen = (await prompt.ShowAsync(console, cancellationToken))!.Highlighted;
        Questions.WriteAnswer(console, Question, chosen.Name, note: chosen.DefaultHostName);
        return chosen;
    }

    /// <summary>
    /// A column for lists of apps: how many slots the app has besides production, e.g. "2 slots" (empty for none, or
    /// when they weren't loaded). It's short, so it goes right after the name, where narrow terminals don't cut it.
    /// </summary>
    public static string SlotCount(AzureWebApp app) => app.Slots?.Count switch
    {
        null or 0 => string.Empty,
        1 => "1 slot",
        var count => $"{count} slots"
    };

    /// <summary>
    /// The name a profile gets for publishing to the app or one of its slots, e.g. <c>contoso</c> or <c>contoso (staging)</c>,
    /// so profiles for different slots can be told apart in the profile list.
    /// </summary>
    public static string ProfileName(AzureWebApp app, AzureDeploymentSlot slot) =>
        slot.IsProduction ? app.Name : $"{app.Name} ({slot.Name})";

    /// <summary>The resource ID of the app, or of the slot (<c>.../sites/{app}/slots/{slot}</c>), as Visual Studio writes it.</summary>
    public static string ResourceId(string subscriptionId, AzureWebApp app, AzureDeploymentSlot slot) =>
        AzureResourceIds.WebApp(subscriptionId, app.ResourceGroup, app.Name, slot.IsProduction ? null : slot.Name);
}
