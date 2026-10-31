using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Publishing.Steps;
using Pubwiz.Core.Targets.Azure.Kudu;

namespace Pubwiz.Core.Targets.Azure;

/// <summary>
/// Deploys to an App Service app or Function App through Kudu's zip ("one deploy") API.
/// </summary>
/// <remarks>
/// Also handles Web Deploy profiles of App Service apps (e.g. imported .publishsettings): their SCM site and
/// publishing credentials accept zip deploys too, which works on every OS, unlike msdeploy.
/// </remarks>
public sealed class AzureAppServiceTarget(DotnetCli dotnet, KuduClient kudu, KuduCredentialsProvider credentials) : IPublishTarget
{
    public PublishTargetType Type => PublishTargetType.AzureAppService;

    public bool CanPublish(PublishProfile profile) => AzureProfile.IsAppService(profile) && !AzureProfile.IsWebJob(profile);

    public bool RequiresPassword(PublishProfile profile) => !AzureProfile.UsesAzCli(profile);

    public IReadOnlyList<PublishStep> CreateSteps(PublishRequest request)
    {
        var zipPath = StagingPublish.GetStagingDirectory(request) + ".zip";
        return
        [
            StagingPublish.CreateStep(dotnet, request),
            new PackageZipStep(zipPath),
            new ZipDeployStep(kudu, credentials, AzureProfile.GetScmUrl(request.Profile)),
            new SetDestinationStep(AzureProfile.GetSiteUrl(request.Profile))
        ];
    }

    /// <summary>Uploads the package and waits until Kudu has deployed it.</summary>
    private sealed class ZipDeployStep(KuduClient kudu, KuduCredentialsProvider credentialsProvider, Uri scmUrl) : PublishStep("Deploy to App Service")
    {
        public override async Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
        {
            var credentials = await credentialsProvider.GetAsync(context, cancellationToken);

            context.ReportDetail($"uploading to {scmUrl.Host}");
            try
            {
                var deployment = await kudu.PublishZipAsync(scmUrl, context.PackagePath!, credentials,
                    onUploadProgress: progress => context.ReportProgress(progress),
                    onStatus: status =>
                    {
                        context.ReportDetail(status.Status.ToString().ToLowerInvariant());
                        context.Log.Info($"Deployment {status.Id}: {status.Status} {status.StatusText}".TrimEnd());
                    },
                    cancellationToken);

                context.Log.Info($"Deployment {deployment.Id} succeeded");
            }
            catch (KuduException ex)
            {
                throw new PublishStepException(ex.Message, ex);
            }
        }
    }
}
