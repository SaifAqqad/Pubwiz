using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Publishing.Steps;
using System.Text.RegularExpressions;

namespace Pubwiz.Core.Targets.Azure.StaticWebApps;

/// <summary>
/// Deploys a Blazor WebAssembly app to Azure Static Web Apps, doing what <c>swa deploy</c> does without Node or the SWA
/// CLI: publish, get the app's deployment token from az (or the profile's saved token), and upload the site's files
/// with Microsoft's StaticSitesClient.
/// </summary>
/// <seealso href="https://github.com/Azure/static-web-apps-cli/blob/main/src/cli/commands/deploy/deploy.ts"/>
public sealed partial class StaticWebAppTarget(DotnetCli dotnet, AzCli az, StaticSitesClientDownloader downloader, IProcessRunner processRunner) : IPublishTarget
{
    /// <summary>The value of <c>PubwizAzureAuthentication</c> for profiles that use a saved deployment token instead of az.</summary>
    public const string DeploymentTokenAuthentication = "DeploymentToken";

    public PublishTargetType Type => PublishTargetType.AzureStaticWebApp;

    public bool CanPublish(PublishProfile profile) =>
        profile.Method == PublishMethod.Custom
        && string.Equals(profile[ProfileProperties.PublishProvider], ProfileProperties.AzureStaticWebAppProvider, StringComparison.OrdinalIgnoreCase);

    /// <summary>The deployment token is the "password" when the profile doesn't use az (e.g. in CI, from <c>PUBWIZ_PASSWORD</c>).</summary>
    public bool RequiresPassword(PublishProfile profile) => UsesDeploymentToken(profile);

    public string? GetSetupError(PublishProfile profile)
    {
        if (StaticSitesClientDownloader.Platform is null)
        {
            return "Microsoft's StaticSitesClient, which uploads to Static Web Apps, only runs on x64 (and Apple silicon Macs).";
        }

        if (UsesDeploymentToken(profile))
        {
            return null;
        }

        if (!az.IsInstalled)
        {
            return $"The Azure CLI (az) isn't installed; it gets the deployment token. Install it ({AzCliInstallGuide.DocsUrl}) or use a deployment token.";
        }

        return profile[ProfileProperties.PubwizAzureResourceGroup] is null || profile[ProfileProperties.PubwizAzureAppName] is null
            ? $"The profile '{profile.Name}' has no {ProfileProperties.PubwizAzureResourceGroup} or {ProfileProperties.PubwizAzureAppName}."
            : null;
    }

    public IReadOnlyList<PublishStep> CreateSteps(PublishRequest request)
    {
        var deploy = new DeployStep(az, downloader, processRunner, StagingPublish.GetStagingDirectory(request) + "-swa");
        return
        [
            StagingPublish.CreateStep(dotnet, request),
            new PrepareSiteStep(),
            deploy,
            new SetDestinationStep(() => deploy.SiteUrl ?? request.Profile[ProfileProperties.SiteUrlToLaunchAfterPublish] ?? request.Profile[ProfileProperties.PubwizAzureAppName]!)
        ];
    }

    private static bool UsesDeploymentToken(PublishProfile profile) =>
        string.Equals(profile[ProfileProperties.PubwizAzureAuthentication], DeploymentTokenAuthentication, StringComparison.OrdinalIgnoreCase);

    /// <summary>Finds the site's files in the publish output and adds the Blazor routing config when the app has none.</summary>
    private sealed class PrepareSiteStep() : PublishStep("Prepare site")
    {
        public override Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
        {
            var output = context.OutputDirectory ?? throw new InvalidOperationException("Nothing was published to deploy.");
            var site = StaticWebAppSite.FindSiteFolder(output);
            if (StaticWebAppSite.EnsureConfig(site))
            {
                context.Log.Info($"Added {StaticWebAppSite.ConfigFileName}: unknown paths load index.html, so the app's routes work when opened directly");
            }

            // Later steps deploy this folder.
            context.OutputDirectory = site;
            return Task.CompletedTask;
        }
    }

    /// <summary>Gets the deployment token and runs StaticSitesClient with the settings <c>swa deploy</c> gives it.</summary>
    private sealed partial class DeployStep(AzCli az, StaticSitesClientDownloader downloader, IProcessRunner processRunner, string workingDirectory)
        : PublishStep("Deploy to Static Web Apps")
    {
        /// <summary>The URL StaticSitesClient reports ("Visit your site at: ..."), which differs per environment.</summary>
        public string? SiteUrl { get; private set; }

        public override async Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
        {
            var profile = context.Request.Profile;
            var token = await GetTokenAsync(context, cancellationToken);
            context.Log.AddSecret(token);

            string client;
            try
            {
                client = await downloader.EnsureAsync(status =>
                {
                    context.ReportDetail(status);
                    context.Log.Info(status);
                }, cancellationToken);
            }
            catch (Exception ex) when (ex is StaticWebAppException or HttpRequestException or IOException)
            {
                throw new PublishStepException(ex.Message, ex);
            }

            // StaticSitesClient writes its zips to the current folder, so it runs in a folder of its own.
            if (Directory.Exists(workingDirectory))
            {
                Directory.Delete(workingDirectory, recursive: true);
            }

            Directory.CreateDirectory(workingDirectory);
            var site = context.OutputDirectory!;
            var environment = profile[ProfileProperties.PubwizStaticWebAppEnvironment];
            var spec = new ProcessSpec(client, [])
            {
                WorkingDirectory = workingDirectory,
                Environment = StaticWebAppSite.CreateClientEnvironment(site, token, environment)
            };

            context.Log.Info($"Uploading {site} to {(environment is null ? "production" : $"the '{environment}' environment")}");
            var errors = new List<string>();
            var result = await processRunner.RunAsync(spec, line => OnOutput(context, line, errors), cancellationToken);

            try
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Leftover zips in obj/ don't matter.
            }

            if (!result.Succeeded)
            {
                throw new PublishStepException(errors.FirstOrDefault() ?? $"StaticSitesClient exited with code {result.ExitCode}");
            }
        }

        private async Task<string> GetTokenAsync(PublishContext context, CancellationToken cancellationToken)
        {
            var profile = context.Request.Profile;
            if (UsesDeploymentToken(profile))
            {
                return context.Request.Password ?? throw new PublishStepException("No deployment token was given.");
            }

            var appName = profile[ProfileProperties.PubwizAzureAppName]!;
            context.ReportDetail($"az staticwebapp secrets list --name {appName}");
            try
            {
                return await az.GetStaticWebAppDeploymentTokenAsync(profile[ProfileProperties.PubwizAzureResourceGroup]!, appName,
                    profile[ProfileProperties.PubwizAzureSubscriptionId], cancellationToken);
            }
            catch (AzCliException ex)
            {
                throw new PublishStepException($"Could not get the deployment token of {appName} from az ({ex.Message}). Run 'az login' and try again.", ex);
            }
        }

        private void OnOutput(PublishContext context, ProcessOutputLine line, List<string> errors)
        {
            // Errors are printed in red, to stdout as well as stderr.
            var isError = line.IsError || line.Text.Contains("\u001b[31m", StringComparison.Ordinal);
            var text = AnsiCodes().Replace(line.Text, string.Empty).Trim();
            if (text.Length == 0)
            {
                return;
            }

            if (SiteUrlLine().Match(text) is { Success: true } match)
            {
                SiteUrl = match.Groups[1].Value;
            }

            if (isError)
            {
                errors.Add(text);
            }

            context.Log.Write(isError ? LogSource.Error : LogSource.Output, text);
            context.ReportDetail(text);
        }

        [GeneratedRegex(@"\u001b\[[0-9;]*m")]
        private static partial Regex AnsiCodes();

        [GeneratedRegex(@"Visit your site at:\s*(https?://\S+)")]
        private static partial Regex SiteUrlLine();
    }
}
