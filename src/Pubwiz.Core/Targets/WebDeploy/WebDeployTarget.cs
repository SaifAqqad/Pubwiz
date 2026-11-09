using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Publishing.Steps;
using Pubwiz.Core.Targets.Azure;
using Pubwiz.Core.Targets.ServerLogins;
using Pubwiz.WebDeploy;

namespace Pubwiz.Core.Targets.WebDeploy;

/// <summary>
/// Deploys to an IIS server with Web Deploy (<c>WebPublishMethod=MSDeploy</c>).
/// </summary>
/// <remarks>
/// It uses the official <c>msdeploy.exe</c> when it's available (Windows with Web Deploy installed) and otherwise
/// the built-in cross-platform <see cref="WebDeployClient"/>, so Linux and macOS can publish too. Web Deploy
/// profiles of Azure App Service apps are handled by <see cref="AzureAppServiceTarget"/> instead, which deploys
/// through Kudu from any OS.
/// <para>
/// When the profile's server has a login saved in <see cref="ServerCredentialStore"/>, that user name and password are
/// used instead of the profile's.
/// </para>
/// </remarks>
public sealed class WebDeployTarget(DotnetCli dotnet, MsDeployCli msdeploy, WebDeployClient client, ServerCredentialStore servers) : IPublishTarget
{
    public PublishTargetType Type => PublishTargetType.WebDeploy;

    public bool CanPublish(PublishProfile profile) => profile.Method == PublishMethod.MSDeploy && !AzureProfile.IsAppService(profile);

    /// <summary>Whether this machine deploys with the official msdeploy.exe (vs the built-in client).</summary>
    public bool UsesMsDeploy => msdeploy.IsInstalled;

    /// <summary>Both msdeploy.exe and the built-in client can preview a sync (<c>-whatif</c>).</summary>
    public bool SupportsWhatIf => true;

    /// <summary>
    /// Without a user name msdeploy signs in as the current Windows user; the built-in client always needs one.
    /// A saved server login brings its own password, so none is asked for.
    /// </summary>
    public bool RequiresPassword(PublishProfile profile) => FindServerLogin(profile) is null && profile[ProfileProperties.UserName] is not null;

    /// <summary>IIS only runs on Windows (Azure App Service profiles go to <see cref="Azure.AzureAppServiceTarget"/>).</summary>
    public TargetPlatform GetPlatform(PublishProfile profile) => TargetPlatform.Of(PublishOs.Windows);

    public string? GetSetupError(PublishProfile profile)
    {
        try
        {
            WebDeployProfile.GetEndpoint(profile);
            WebDeployProfile.GetSkipRules(profile);
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }

        // msdeploy.exe publishes any profile; the built-in client needs a Basic-auth user name and password,
        // from the profile or a saved server login.
        return UsesMsDeploy || FindServerLogin(profile) is not null ? null : WebDeployProfile.BuiltInClientBlocker(profile);
    }

    /// <summary>The login saved for the profile's server, which replaces the profile's user name and password; <c>null</c> when there's none.</summary>
    public ServerCredential? FindServerLogin(PublishProfile profile) =>
        WebDeployAlternatives.TryGetEndpoint(profile) is { } endpoint ? servers.Find(endpoint.ServiceUrl.Host) : null;

    public IReadOnlyList<PublishStep> CreateSteps(PublishRequest request)
    {
        var endpoint = WebDeployProfile.GetEndpoint(request.Profile);
        var savedLogin = FindServerLogin(request.Profile) is { } saved ? new SavedServerLogin(servers, saved) : null;
        var login = new LoginSource(savedLogin);
        var stepName = request.WhatIf ? "Preview changes (Web Deploy)" : "Deploy (Web Deploy)";
        PublishStep sync = UsesMsDeploy ? new SyncStep(stepName, msdeploy, endpoint, login) : new BuiltInSyncStep(stepName, client, endpoint, login);

        // A preview deploys nothing, so its result is the change count the sync step reports, not the site.
        if (request.WhatIf)
        {
            return [StagingPublish.CreateStep(dotnet, request), sync];
        }

        return
        [
            StagingPublish.CreateStep(dotnet, request),
            sync,
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
            AllowUntrustedCertificate = request.AllowUntrustedCertificate ?? WebDeployProfile.AllowsUntrustedCertificate(profile),
            KeepExtraFiles = IsTrue(profile[ProfileProperties.SkipExtraFilesOnServer]),
            // A running app keeps its DLLs locked, so the app is taken offline unless turned off for the run.
            TakeAppOffline = request.TakeAppOffline ?? true,
            UseChecksum = WebDeployProfile.UsesChecksum(profile),
            WhatIf = request.WhatIf,
            SkipRules = WebDeployProfile.GetSkipRules(profile),
            Verbose = request.Verbose
        };
    }

    private static bool IsTrue(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Where a sync step gets its login: the profile and request, or the server's saved login when there is one.</summary>
    /// <param name="SavedLogin">The server's saved login, or <c>null</c> to use the profile's.</param>
    internal sealed record LoginSource(SavedServerLogin? SavedLogin)
    {
        /// <summary>The sync settings for the running step, signed in with the saved server login when there is one.</summary>
        public async Task<MsDeploySyncOptions> CreateSyncOptionsAsync(PublishContext context, WebDeployEndpoint endpoint, CancellationToken cancellationToken)
        {
            var options = CreateSyncOptions(context.Request, endpoint, context.OutputDirectory!);
            if (SavedLogin is null)
            {
                return options;
            }

            var password = await SavedLogin.GetPasswordAsync(context, cancellationToken);
            return options with { UserName = SavedLogin.UserName, Password = password, AuthType = "Basic" };
        }
    }

    /// <summary>Runs <c>msdeploy -verb:sync</c> from the staging folder to the IIS application.</summary>
    private sealed class SyncStep(string name, MsDeployCli msdeploy, WebDeployEndpoint endpoint, LoginSource login) : PublishStep(name)
    {
        public override async Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
        {
            var options = await login.CreateSyncOptionsAsync(context, endpoint, cancellationToken);
            context.Log.AddSecret(options.Password);

            try
            {
                await SyncAsync(context, options, cancellationToken);
                if (options.WhatIf)
                {
                    // msdeploy prints the changes it would make, and "Total changes: …", in the log.
                    context.Destination = "preview only, nothing deployed (the changes are in the log)";
                }
            }
            catch (MsDeployException ex) when (ex.ErrorCode == MsDeployException.FileInUse && options.TakeAppOffline)
            {
                // The app was taken offline (app_offline.htm) but still had a file locked: give it a moment to shut down.
                context.Log.Info($"A file is in use; retrying in {FileInUseRetryDelay.TotalSeconds:0}s");
                context.ReportDetail("a file is in use, retrying...");
                await Task.Delay(FileInUseRetryDelay, cancellationToken);
                await RetryAsync(context, options, cancellationToken);
            }
            catch (MsDeployException ex) when (ex.ErrorCode == MsDeployException.FileInUse)
            {
                // --no-app-offline was given, so the app keeps running and its files stay locked.
                throw new PublishStepException($"{ex.Message} The running app has the file locked; publish without --no-app-offline to take it offline while copying.", ex);
            }
            catch (MsDeployException ex) when (ex.ErrorCode == MsDeployException.CertificateValidationFailed)
            {
                throw new PublishStepException($"{ex.Message} {WebDeployProfile.UntrustedCertificateHint}", ex);
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

    /// <summary>Syncs the staging folder to the IIS application with the built-in client (no msdeploy.exe).</summary>
    private sealed class BuiltInSyncStep(string name, WebDeployClient client, WebDeployEndpoint endpoint, LoginSource login) : PublishStep(name)
    {
        public override async Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
        {
            var options = await login.CreateSyncOptionsAsync(context, endpoint, cancellationToken);
            context.Log.AddSecret(options.Password);

            // GetSetupError already rejected profiles without a user name and password, so these are set here.
            if (options.UserName is null || options.Password is null)
            {
                throw new PublishStepException("Web Deploy needs a user name and password to publish from this machine.");
            }

            var credentials = new WebDeployCredentials(options.UserName, options.Password);
            var clientOptions = ToClientOptions(options, context);

            context.ReportDetail($"{(options.WhatIf ? "previewing" : "syncing to")} {endpoint.ServiceUrl.Host}");
            try
            {
                var result = await client.SyncFolderAsync(endpoint, credentials, options.SourceDirectory, clientOptions, cancellationToken);
                if (result.WhatIf)
                {
                    context.Log.Info($"Preview: Web Deploy would make {result.ChangeCount} change(s); nothing was deployed.");
                    context.Destination = $"preview: {result.ChangeCount} change(s), nothing deployed";
                }
                else
                {
                    context.Log.Info($"Web Deploy applied {result.ChangeCount} change(s).");
                }
            }
            catch (WebDeployException ex)
            {
                var message = ex.IsUntrustedCertificate ? $"{ex.Message}. {WebDeployProfile.UntrustedCertificateHint}" : ex.Message;
                throw new PublishStepException(message, ex);
            }
        }

        /// <summary>The same settings msdeploy.exe gets, for the built-in client.</summary>
        private static WebDeploySyncOptions ToClientOptions(MsDeploySyncOptions options, PublishContext context)
        {
            var clientOptions = new WebDeploySyncOptions
            {
                AllowUntrusted = options.AllowUntrustedCertificate,
                TakeAppOffline = options.TakeAppOffline,
                DeleteExtraFiles = !options.KeepExtraFiles,
                UseChecksum = options.UseChecksum,
                WhatIf = options.WhatIf,
                OnProgress = progress => LogProgress(context, progress)
            };

            foreach (var rule in options.SkipRules)
            {
                clientOptions.SkipRules.Add(rule);
            }

            return clientOptions;
        }

        private static void LogProgress(PublishContext context, WebDeployProgress progress)
        {
            context.Log.Write(LogSource.Output, progress.Message);
            context.ReportDetail(progress.Message);
        }
    }
}
