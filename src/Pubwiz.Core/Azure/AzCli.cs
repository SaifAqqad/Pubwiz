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

    private readonly Lazy<string?> _executablePath = new(() => ExecutableLocator.FindInPath("az"));

    public bool IsInstalled => _executablePath.Value is not null;

    /// <returns>The active subscription, or <c>null</c> when nobody is signed in.</returns>
    public async Task<AzureSubscription?> GetCurrentAccountAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["account", "show", "-o", "json"], onOutput: null, cancellationToken);
        return result.Succeeded ? Deserialize<AzureSubscription>(result.Output) : null;
    }

    /// <summary>
    /// Signs in with <c>az login</c>. The browser flow needs a desktop; the device code flow prints a code and URL
    /// to stderr, which is streamed to <paramref name="onOutput"/> so the user can see it.
    /// </summary>
    public async Task LoginAsync(bool useDeviceCode, string? tenant, Action<ProcessOutputLine> onOutput, CancellationToken cancellationToken = default)
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

        var result = await RunAsync(arguments, onOutput, cancellationToken);
        EnsureSucceeded(result, "az login");
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

    /// <summary>App Service apps (including Function Apps) in the active subscription.</summary>
    public async Task<IReadOnlyList<AzureWebApp>> ListWebAppsAsync(CancellationToken cancellationToken = default)
    {
        const string query = "[].{name:name, resourceGroup:resourceGroup, kind:kind, defaultHostName:defaultHostName, state:state}";
        var result = await RunAsync(["webapp", "list", "--query", query, "-o", "json"], onOutput: null, cancellationToken);
        EnsureSucceeded(result, "az webapp list");
        return Deserialize<List<AzureWebApp>>(result.Output) ?? [];
    }

    /// <summary>An Entra ID access token for Azure Resource Manager, which Kudu also accepts.</summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["account", "get-access-token", "--resource", ManagementResource, "--query", "accessToken", "-o", "tsv"], onOutput: null, cancellationToken);
        EnsureSucceeded(result, "az account get-access-token");
        return result.Output.Trim();
    }

    private Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, Action<ProcessOutputLine>? onOutput, CancellationToken cancellationToken)
    {
        var executable = _executablePath.Value ?? throw new PrerequisiteMissingException("Azure (az)", "Install it from https://aka.ms/installazurecli.");

        // Keep az from asking questions or printing upgrade notices in the middle of the output.
        var spec = new ProcessSpec(executable, arguments)
        {
            Environment = new Dictionary<string, string?> { ["AZURE_CORE_ONLY_SHOW_ERRORS"] = "true", ["AZURE_CORE_NO_COLOR"] = "true" }
        };

        return processRunner.RunAsync(spec, onOutput, cancellationToken);
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
