using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Publishing.Steps;

namespace Pubwiz.Core.Targets.Aws;

/// <summary>
/// Deploys to an existing Elastic Beanstalk environment (Linux or Windows Server): publishes, zips the app into a
/// source bundle, uploads it to Elastic Beanstalk's S3 bucket as a new application version, and deploys that version.
/// </summary>
/// <remarks>The same steps as AWS's own <c>dotnet aws deploy</c> for an existing environment, through the AWS CLI.</remarks>
/// <param name="dotnet">The dotnet CLI.</param>
/// <param name="aws">The AWS CLI.</param>
/// <param name="pollInterval">How often to check on the deployment (5 seconds by default; shorter in tests).</param>
public sealed class ElasticBeanstalkTarget(DotnetCli dotnet, AwsCli aws, TimeSpan? pollInterval = null) : IPublishTarget
{
    public PublishTargetType Type => PublishTargetType.AwsElasticBeanstalk;

    public bool CanPublish(PublishProfile profile) => AwsProfile.IsElasticBeanstalk(profile);

    public bool RequiresPassword(PublishProfile profile) => false;

    public string? GetSetupError(PublishProfile profile) =>
        AwsProfile.GetSetupError(profile, aws, ProfileProperties.PubwizAwsApplication, ProfileProperties.PubwizAwsEnvironment);

    public IReadOnlyList<PublishStep> CreateSteps(PublishRequest request)
    {
        var profile = request.Profile;
        var release = new BeanstalkRelease(
            AwsProfile.GetLocation(profile),
            profile[ProfileProperties.PubwizAwsApplication]!,
            profile[ProfileProperties.PubwizAwsEnvironment]!,
            CreateVersionLabel(request, DateTimeOffset.UtcNow));

        var zipPath = StagingPublish.GetStagingDirectory(request) + ".zip";
        var windows = string.Equals(profile[ProfileProperties.PubwizAwsWindows], "true", StringComparison.OrdinalIgnoreCase);

        return
        [
            StagingPublish.CreateStep(dotnet, request),
            new BundleStep(zipPath, windows),
            new UploadVersionStep(aws, release),
            new DeployVersionStep(aws, release, pollInterval ?? TimeSpan.FromSeconds(5)),
            new SetDestinationStep(profile[ProfileProperties.SiteUrlToLaunchAfterPublish] ?? $"{release.Application}/{release.Environment}")
        ];
    }

    /// <summary>
    /// A unique label for the new application version, e.g. <c>Shop-20261102-153000-2a2755a</c>: the project, the time and,
    /// in a git repository, the commit (Elastic Beanstalk shows it in the console's version list).
    /// </summary>
    internal static string CreateVersionLabel(PublishRequest request, DateTimeOffset now)
    {
        var label = $"{request.Project.Name}-{now.UtcDateTime:yyyyMMdd-HHmmss}";
        if (request.Git is { } git)
        {
            label += $"-{git.ShortCommit}";
        }

        // Labels can be at most 100 characters.
        return label.Length <= 100 ? label : label[^100..];
    }

    /// <summary>The application version being released, shared by the upload and deploy steps.</summary>
    private sealed record BeanstalkRelease(AwsLocation Location, string Application, string Environment, string VersionLabel)
    {
        public string Bucket { get; set; } = string.Empty;

        /// <summary>The bundle's place in the bucket, next to the versions the console and EB CLI upload.</summary>
        public string Key => $"{Application}/{VersionLabel}.zip";
    }

    private sealed class BundleStep(string zipPath, bool windows) : PublishStep("Package")
    {
        public override Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
        {
            var folder = context.OutputDirectory ?? throw new InvalidOperationException("Nothing was published to package.");
            if (BeanstalkBundle.Create(folder, zipPath, windows) is { } added)
            {
                context.Log.Info(added);
            }

            context.PackagePath = zipPath;
            context.Log.Info($"Created {zipPath} ({new FileInfo(zipPath).Length / 1024d / 1024d:0.0} MB)");
            return Task.CompletedTask;
        }
    }

    /// <summary>Uploads the bundle to S3 and registers it as an application version.</summary>
    private sealed class UploadVersionStep(AwsCli aws, BeanstalkRelease release) : PublishStep("Upload version")
    {
        public override async Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
        {
            try
            {
                // Checked first: a deployment can't start while another one is running.
                var environment = await aws.GetBeanstalkEnvironmentAsync(release.Location, release.Application, release.Environment, cancellationToken);
                if (environment.IsBusy)
                {
                    throw new PublishStepException($"{release.Environment} is busy ({environment.Status}). Try again when it's Ready.");
                }

                context.ReportDetail("finding the Elastic Beanstalk bucket");
                release.Bucket = await aws.GetBeanstalkBucketAsync(release.Location, cancellationToken);

                context.ReportDetail($"s3://{release.Bucket}/{release.Key}");
                await aws.UploadToS3Async(release.Location, context.PackagePath!, release.Bucket, release.Key, cancellationToken);
                context.Log.Info($"Uploaded s3://{release.Bucket}/{release.Key}");

                var description = context.Request.Git is { } git ? $"{git.ShortCommit} {git.Subject}" : "Published with Pubwiz";
                await aws.CreateBeanstalkVersionAsync(release.Location, release.Application, release.VersionLabel, release.Bucket, release.Key,
                    description.Length <= 200 ? description : description[..200], cancellationToken);
                context.Log.Info($"Created version {release.VersionLabel} of {release.Application}");
            }
            catch (AwsCliException ex)
            {
                throw new PublishStepException(ex.Message, ex);
            }
        }
    }

    /// <summary>Deploys the version and follows the environment's events until it's Ready again.</summary>
    private sealed class DeployVersionStep(AwsCli aws, BeanstalkRelease release, TimeSpan pollInterval) : PublishStep("Deploy to Elastic Beanstalk")
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

        public override async Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
        {
            try
            {
                // Events are read from just before the deployment on, so older ones aren't shown again.
                var since = DateTimeOffset.UtcNow.AddSeconds(-30);
                await aws.DeployBeanstalkVersionAsync(release.Location, release.Application, release.Environment, release.VersionLabel, cancellationToken);
                context.Log.Info($"Deploying {release.VersionLabel} to {release.Environment}");

                var errors = new List<string>();
                var deadline = DateTimeOffset.UtcNow + Timeout;
                BeanstalkEnvironment environment;
                do
                {
                    await Task.Delay(pollInterval, cancellationToken);
                    environment = await aws.GetBeanstalkEnvironmentAsync(release.Location, release.Application, release.Environment, cancellationToken);

                    foreach (var item in await aws.GetBeanstalkEventsAsync(release.Location, release.Application, release.Environment, since, cancellationToken))
                    {
                        since = item.Date;
                        context.Log.Write(item.IsError ? LogSource.Error : LogSource.Output, $"{item.Severity}: {item.Message}");
                        context.ReportDetail(item.Message);
                        if (item.IsError)
                        {
                            errors.Add(item.Message);
                        }
                    }

                    if (DateTimeOffset.UtcNow > deadline)
                    {
                        throw new PublishStepException($"{release.Environment} is still {environment.Status} after {Timeout.TotalMinutes:0} minutes. See its events in the AWS console.");
                    }
                }
                while (environment.IsBusy);

                if (errors.Count > 0 || environment.VersionLabel != release.VersionLabel)
                {
                    var reason = errors.FirstOrDefault() ?? $"the environment runs {environment.VersionLabel} instead";
                    throw new PublishStepException($"The deployment failed: {reason}");
                }

                context.Log.Info($"{release.Environment} runs {release.VersionLabel} (health: {environment.Health ?? "unknown"})");
            }
            catch (AwsCliException ex)
            {
                throw new PublishStepException(ex.Message, ex);
            }
        }
    }
}
