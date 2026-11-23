using Pubwiz.Core.Targets.Azure;

namespace Pubwiz.Tool.Azure;

/// <summary>
/// The App Service apps and Function Apps of a subscription, and their deployment slots, loaded once per run: creating
/// profiles for several projects (e.g. a batch) lists them again without waiting for az.
/// </summary>
/// <remarks>
/// The first load is one Azure Resource Graph query, which has every app and slot, so choosing a slot needs no more
/// az calls. Resource Graph is updated with a short delay (usually under a minute), so the lists' Refresh row calls
/// <see cref="LoadAppsAsync"/>, which asks App Service itself and is always up to date; the slots of the app that's
/// chosen are then asked for too. The same happens when the Resource Graph query fails.
/// </remarks>
public sealed class AzureAppList(AzCli az)
{
    private readonly Dictionary<string, IReadOnlyList<AzureWebApp>> _apps = [];
    private readonly Dictionary<string, IReadOnlyList<AzureDeploymentSlot>> _slots = [];

    /// <summary>The apps with their slots, loaded the first time they're needed in this run.</summary>
    public async Task<IReadOnlyList<AzureWebApp>> GetAppsAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        if (!_apps.TryGetValue(subscriptionId, out var apps))
        {
            _apps[subscriptionId] = apps = await TryQueryResourceGraphAsync(subscriptionId, cancellationToken)
                                           ?? await az.ListWebAppsAsync(subscriptionId, cancellationToken);
        }

        return apps;
    }

    /// <summary>
    /// Loads the apps again from App Service (always up to date), and forgets the slots loaded so far, since they may
    /// have changed too: they're asked for again when an app is chosen.
    /// </summary>
    public async Task<IReadOnlyList<AzureWebApp>> LoadAppsAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        var apps = await az.ListWebAppsAsync(subscriptionId, cancellationToken);
        _apps[subscriptionId] = apps;
        _slots.Clear();
        return apps;
    }

    /// <summary>
    /// The app's deployment slots (production isn't one of them): the ones loaded with the app, or else asked for the
    /// first time they're needed in this run.
    /// </summary>
    public async Task<IReadOnlyList<AzureDeploymentSlot>> GetSlotsAsync(AzureWebApp app, CancellationToken cancellationToken)
    {
        if (app.Slots is { } loaded)
        {
            return loaded;
        }

        var key = $"{app.ResourceGroup}/{app.Name}".ToLowerInvariant();
        if (!_slots.TryGetValue(key, out var slots))
        {
            _slots[key] = slots = await az.ListDeploymentSlotsAsync(app.ResourceGroup, app.Name, cancellationToken);
        }

        return slots;
    }

    /// <returns>The apps, or <c>null</c> when Resource Graph can't be used (e.g. not allowed): App Service is asked instead.</returns>
    private async Task<IReadOnlyList<AzureWebApp>?> TryQueryResourceGraphAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        try
        {
            return await az.ListWebAppsWithSlotsAsync(subscriptionId, cancellationToken);
        }
        catch (AzCliException)
        {
            return null;
        }
    }
}
