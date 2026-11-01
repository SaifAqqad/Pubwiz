using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Publishing.Steps;
using Pubwiz.Core.Targets.Azure;
using Pubwiz.WebDeploy;

namespace Pubwiz.Core.Targets.WebDeploy;

/// <summary>
/// Deploys to an IIS server with Web Deploy (<c>WebPublishMethod=MSDeploy</c>), using the official msdeploy.exe.
/// </summary>
/// <remarks>
/// Web Deploy profiles of Azure App Service apps are handled by <see cref="AzureAppServiceTarget"/> instead,
/// which deploys through Kudu from any OS.
/// </remarks>
public sealed class WebDeployTarget(DotnetCli dotnet, MsDeployCli msdeploy) : IPublishTarget
{
    public PublishTargetType Type => PublishTargetType.WebDeploy;

    public bool CanPublish(PublishProfile profile) => profile.Method == PublishMethod.MSDeploy && !AzureProfile.IsAppService(profile);

    /// <summary>Without a user name, msdeploy signs in as the current Windows user.</summary>
    public bool RequiresPassword(PublishProfile profile) => profile[ProfileProperties.UserName] is not null;

    public string? GetSetupError(PublishProfile profile)
    {
        if (!msdeploy.IsInstalled)
        {
            return MsDeployLocator.MissingReason;
        }

        try
        {
            WebDeployProfile.GetEndpoint(profile);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    public IReadOnlyList<PublishStep> CreateSteps(PublishRequest request)
    {
        var endpoint = WebDeployProfile.GetEndpoint(request.Profile);
        return
        [
            StagingPublish.CreateStep(dotnet, request),
            new SyncStep(msdeploy, endpoint),
            new SetDestinationStep(request.Profile[ProfileProperties.SiteUrlToLaunchAfterPublish] ?? $"{endpoint.ServiceUrl.Host}/{endpoint.IisPath}")
        ];
    }

    /// <summary>The msdeploy settings for a request, read from the same profile properties Visual Studio writes.</summary>
    internal static MsDeploySyncOptions CreateSyncOptions(PublishRequest request, WebDeployEndpoint endpoint, string sourceDirectory)
    {
        var profile = request.Profile;
        return new MsDeploySyncOptions(sourceDirectory, endpoint)
        {
            UserName = profile[ProfileProperties.UserName],
            Password = request.Password,
            AuthType = profile[ProfileProperties.AuthType],
            AllowUntrustedCertificate = IsTrue(profile[ProfileProperties.AllowUntrustedCertificate]),
            KeepExtraFiles = IsTrue(profile[ProfileProperties.SkipExtraFilesOnServer]),
            TakeAppOffline = request.TakeAppOffline ?? (IsTrue(profile[ProfileProperties.EnableMSDeployAppOffline]) || IsTrue(profile[ProfileProperties.PubwizUseAppOffline])),
            Verbose = request.Verbose
        };
    }

    private static bool IsTrue(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Runs <c>msdeploy -verb:sync</c> from the staging folder to the IIS application.</summary>
    private sealed class SyncStep(MsDeployCli msdeploy, WebDeployEndpoint endpoint) : PublishStep("Deploy (Web Deploy)")
    {
        public override async Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
        {
            var options = CreateSyncOptions(context.Request, endpoint, context.OutputDirectory!);
            context.Log.AddSecret(options.Password);

            try
            {
                await SyncAsync(context, options, cancellationToken);
            }
            catch (MsDeployException ex) when (ex.ErrorCode == MsDeployException.FileInUse)
            {
                // The running app has a file locked. Like Visual Studio suggests, take the app offline (app_offline.htm)
                // so IIS releases it; if it already was, give it a moment to shut down.
                if (options.TakeAppOffline)
                {
                    context.Log.Info($"A file is in use; retrying in {FileInUseRetryDelay.TotalSeconds:0}s");
                    context.ReportDetail("a file is in use, retrying...");
                    await Task.Delay(FileInUseRetryDelay, cancellationToken);
                }
                else
                {
                    context.Log.Info("A file is in use; retrying with the app taken offline (AppOffline rule)");
                    context.ReportDetail("a file is in use, taking the app offline...");
                    options = options with { TakeAppOffline = true };
                }

                await RetryAsync(context, options, cancellationToken);
            }
            catch (MsDeployException ex)
            {
                throw new PublishStepException(ex.Message, ex);
            }
        }

        private static readonly TimeSpan FileInUseRetryDelay = TimeSpan.FromSeconds(5);

        private async Task RetryAsync(PublishContext context, MsDeploySyncOptions options, CancellationToken cancellationToken)
        {
            try
            {
                await SyncAsync(context, options, cancellationToken);
            }
            catch (MsDeployException ex)
            {
                var hint = ex.ErrorCode == MsDeployException.FileInUse ? " Restart the app (or its app pool) and publish again." : string.Empty;
                throw new PublishStepException(ex.Message + hint, ex);
            }
        }

        private async Task SyncAsync(PublishContext context, MsDeploySyncOptions options, CancellationToken cancellationToken)
        {
            context.Log.Info($"msdeploy {MsDeployCli.BuildSyncCommandLine(options)}");
            context.ReportDetail($"syncing to {endpoint.ServiceUrl.Host}");
            await msdeploy.SyncAsync(options, line => LogOutput(context, line.Text), cancellationToken);
        }

        private static void LogOutput(PublishContext context, string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            // msdeploy writes "Info: Adding file (site\app.dll)." per change, and "Error..." lines on failure.
            var isError = line.StartsWith("Error", StringComparison.Ordinal) || line.StartsWith("More Information", StringComparison.Ordinal);
            context.Log.Write(isError ? LogSource.Error : LogSource.Output, line);
            context.ReportDetail(line.StartsWith("Info: ", StringComparison.Ordinal) ? line["Info: ".Length..] : line);
        }
    }
}
