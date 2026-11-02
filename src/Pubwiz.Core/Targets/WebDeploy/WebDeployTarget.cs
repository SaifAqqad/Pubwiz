using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Publishing.Steps;
using Pubwiz.Core.Targets.Azure;
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

    /// <summary>
    /// Without a user name msdeploy signs in as the current Windows user; the built-in client always needs one.
    /// A saved server login brings its own password, so none is asked for.
    /// </summary>
    public bool RequiresPassword(PublishProfile profile) => FindServerLogin(profile) is null && profile[ProfileProperties.UserName] is not null;

    public string? GetSetupError(PublishProfile profile)
    {
        try
        {
            WebDeployProfile.GetEndpoint(profile);
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
        var login = new LoginSource(servers, FindServerLogin(request.Profile));
        PublishStep sync = UsesMsDeploy ? new SyncStep(msdeploy, endpoint, login) : new BuiltInSyncStep(client, endpoint, login);
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
            Verbose = request.Verbose
        };
    }

    private static bool IsTrue(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Where a sync step gets its login: the profile and request, or the server's saved login when there is one.</summary>
    /// <param name="Servers">Holds the saved passwords.</param>
    /// <param name="ServerLogin">The server's saved login, or <c>null</c> to use the profile's.</param>
    internal sealed record LoginSource(ServerCredentialStore Servers, ServerCredential? ServerLogin)
    {
        /// <summary>The sync settings for the running step, signed in with the saved server login when there is one.</summary>
        public async Task<MsDeploySyncOptions> CreateSyncOptionsAsync(PublishContext context, WebDeployEndpoint endpoint, CancellationToken cancellationToken)
        {
            var options = CreateSyncOptions(context.Request, endpoint, context.OutputDirectory!);
            if (ServerLogin is null)
            {
                return options;
            }

            string? password;
            try
            {
                password = await Servers.GetPasswordAsync(ServerLogin, cancellationToken);
            }
            catch (Infrastructure.Secrets.SecretStoreException ex)
            {
                throw new PublishStepException($"The saved password for {ServerLogin.Server} couldn't be read from {Servers.SecretStoreName}: {ex.Message}", ex);
            }

            if (password is null)
            {
                throw new PublishStepException(
                    $"The password for {ServerLogin.Server} is missing from {Servers.SecretStoreName}. Save it again with: dotnet pubwiz servers set {ServerLogin.Server}");
            }

            context.Log.Info($"Signing in as {ServerLogin.UserName}, the login saved for {ServerLogin.Server} (instead of the profile's)");
            return options with { UserName = ServerLogin.UserName, Password = password, AuthType = "Basic" };
        }
    }

    /// <summary>Runs <c>msdeploy -verb:sync</c> from the staging folder to the IIS application.</summary>
    private sealed class SyncStep(MsDeployCli msdeploy, WebDeployEndpoint endpoint, LoginSource login) : PublishStep("Deploy (Web Deploy)")
    {
        public override async Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
        {
            var options = await login.CreateSyncOptionsAsync(context, endpoint, cancellationToken);
            context.Log.AddSecret(options.Password);

            try
            {
                await SyncAsync(context, options, cancellationToken);
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
    private sealed class BuiltInSyncStep(WebDeployClient client, WebDeployEndpoint endpoint, LoginSource login) : PublishStep("Deploy (Web Deploy)")
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

            NoteUnsupportedOptions(context, options);

            var credentials = new WebDeployCredentials(options.UserName, options.Password);
            var clientOptions = new WebDeploySyncOptions
            {
                AllowUntrusted = options.AllowUntrustedCertificate,
                OnProgress = progress => LogProgress(context, progress)
            };

            context.ReportDetail($"syncing to {endpoint.ServiceUrl.Host}");
            try
            {
                var result = await client.SyncFolderAsync(endpoint, credentials, options.SourceDirectory, clientOptions, cancellationToken);
                context.Log.Info($"Web Deploy applied {result.ChangeCount} change(s).");
            }
            catch (WebDeployException ex)
            {
                var message = ex.IsUntrustedCertificate ? $"{ex.Message}. {WebDeployProfile.UntrustedCertificateHint}" : ex.Message;
                throw new PublishStepException(message, ex);
            }
        }

        /// <summary>Tells the user which msdeploy.exe-only options the built-in client leaves unapplied.</summary>
        private static void NoteUnsupportedOptions(PublishContext context, MsDeploySyncOptions options)
        {
            if (!options.KeepExtraFiles)
            {
                context.Log.Info("The built-in Web Deploy client doesn't delete extra files on the server; files that aren't part of the app are kept.");
            }

            if (options.TakeAppOffline)
            {
                context.Log.Info("The built-in Web Deploy client can't take the app offline yet; if a file is in use, stop the app or its app pool first.");
            }
        }

        private static void LogProgress(PublishContext context, WebDeployProgress progress)
        {
            context.Log.Write(LogSource.Output, progress.Message);
            context.ReportDetail(progress.Message);
        }
    }
}
