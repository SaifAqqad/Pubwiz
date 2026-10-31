using Pubwiz.Core.Platform;
using Pubwiz.Core.Processes;
using System.Text.Json;

namespace Pubwiz.Core.Azure;

/// <summary>
/// Thrown when an <c>az</c> command fails.
/// </summary>
public sealed class AzCliException(string message) : Exception(message);

/// <summary>
/// Wrapper around the Azure CLI (<c>az</c>), used for signing in, finding resources and getting access tokens.
/// </summary>
public sealed class AzCli(IProcessRunner processRunner)
{
    /// <summary>The resource Kudu (SCM) access tokens are issued for.</summary>
    private const string ManagementResource = "https://management.azure.com/";

    private Lazy<string?> _executablePath = new(() => ExecutableLocator.FindInPath("az"));

    public bool IsInstalled => _executablePath.Value is not null;

    /// <summary>Looks for az again, e.g. right after installing it.</summary>
    /// <returns><c>true</c> when it's installed now.</returns>
    public bool Refresh()
    {
        _executablePath = new Lazy<string?>(() => ExecutableLocator.FindInPath("az"));
        return IsInstalled;
    }

    /// <returns>The active subscription, or <c>null</c> when nobody is signed in.</returns>
    public async Task<AzureSubscription?> GetCurrentAccountAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["account", "show", "-o", "json"], onOutput: null, cancellationToken);
        return result.Succeeded ? Deserialize<AzureSubscription>(result.Output) : null;
    }

    /// <summary>
    /// Signs in with <c>az login</c>, attached to the terminal so the user sees the browser prompt or the device code.
    /// </summary>
    /// <param name="useDeviceCode">Show a code to enter on another device instead of opening a browser (SSH sessions, no desktop).</param>
    /// <param name="tenant">Sign in to a specific tenant (ID or domain).</param>
    /// <param name="cancellationToken">Cancels the login.</param>
    public async Task LoginAsync(bool useDeviceCode, string? tenant, CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["login", "-o", "none"];
        if (useDeviceCode)
        {
            arguments.Add("--use-device-code");
        }

        if (tenant is not null)
        {
            arguments.AddRange(["--tenant", tenant]);
        }

        var exitCode = await processRunner.RunInteractiveAsync(CreateSpec(arguments), cancellationToken);
        if (exitCode != 0)
        {
            throw new AzCliException($"az login failed with exit code {exitCode}");
        }
    }

    public async Task<IReadOnlyList<AzureSubscription>> ListSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["account", "list", "-o", "json"], onOutput: null, cancellationToken);
        EnsureSucceeded(result, "az account list");
        return Deserialize<List<AzureSubscription>>(result.Output) ?? [];
    }

    public async Task SetSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["account", "set", "--subscription", subscriptionId], onOutput: null, cancellationToken);
        EnsureSucceeded(result, "az account set");
    }

    /// <summary>App Service apps and Function Apps in the active subscription, sorted by name.</summary>
    /// <remarks><c>az webapp list</c> leaves Function Apps out, so both lists are read and merged.</remarks>
    public async Task<IReadOnlyList<AzureWebApp>> ListWebAppsAsync(CancellationToken cancellationToken = default)
    {
        const string query = "[].{name:name, resourceGroup:resourceGroup, kind:kind, defaultHostName:defaultHostName, state:state}";

        var webApps = RunAsync(["webapp", "list", "--query", query, "-o", "json"], onOutput: null, cancellationToken);
        var functionApps = RunAsync(["functionapp", "list", "--query", query, "-o", "json"], onOutput: null, cancellationToken);
        var results = await Task.WhenAll(webApps, functionApps);

        EnsureSucceeded(results[0], "az webapp list");
        EnsureSucceeded(results[1], "az functionapp list");

        return results
            .SelectMany(r => Deserialize<List<AzureWebApp>>(r.Output) ?? [])
            .DistinctBy(app => (app.ResourceGroup.ToLowerInvariant(), app.Name.ToLowerInvariant()))
            .OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>An Entra ID access token for Azure Resource Manager, which Kudu also accepts.</summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["account", "get-access-token", "--resource", ManagementResource, "--query", "accessToken", "-o", "tsv"], onOutput: null, cancellationToken);
        EnsureSucceeded(result, "az account get-access-token");
        return result.Output.Trim();
    }

    private Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, Action<ProcessOutputLine>? onOutput, CancellationToken cancellationToken) =>
        processRunner.RunAsync(CreateSpec(arguments), onOutput, cancellationToken);

    private ProcessSpec CreateSpec(IReadOnlyList<string> arguments)
    {
        var executable = _executablePath.Value ?? throw new PrerequisiteMissingException("Azure (az)", $"Install it: {AzCliInstallGuide.DocsUrl}");

        // Keep az from printing upgrade notices and colors in the middle of the output.
        return new ProcessSpec(executable, arguments)
        {
            Environment = new Dictionary<string, string?> { ["AZURE_CORE_ONLY_SHOW_ERRORS"] = "true", ["AZURE_CORE_NO_COLOR"] = "true" }
        };
    }


    private static void EnsureSucceeded(ProcessResult result, string command)
    {
        if (!result.Succeeded)
        {
            var error = result.Error.Trim();
            throw new AzCliException(error.Length > 0 ? error : $"{command} failed with exit code {result.ExitCode}");
        }
    }

    internal static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json);
}
