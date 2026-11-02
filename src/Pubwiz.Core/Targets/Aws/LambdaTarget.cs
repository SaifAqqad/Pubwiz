using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Publishing.Steps;
using System.IO.Compression;

namespace Pubwiz.Core.Targets.Aws;

/// <summary>
/// Updates the code of an existing Lambda function: publishes for Linux (the function's architecture), zips the app
/// and uploads it, like <c>dotnet lambda deploy-function</c> does for a function that already exists.
/// </summary>
/// <remarks>
/// The profile holds the runtime identifier (<c>linux-x64</c> or <c>linux-arm64</c>) and, for custom runtimes
/// (<c>provided.al2023</c>, e.g. Native AOT), <c>SelfContained=true</c>; the function's handler and settings are left as
/// they are.
/// </remarks>
public sealed class LambdaTarget(DotnetCli dotnet, AwsCli aws) : IPublishTarget
{
    /// <summary>The file a custom runtime starts.</summary>
    public const string BootstrapFileName = "bootstrap";

    /// <summary>Lambda's limit for a zip uploaded directly (larger ones have to go through S3).</summary>
    private const long MaxDirectUploadBytes = 50L * 1024 * 1024;

    public PublishTargetType Type => PublishTargetType.AwsLambda;

    public bool CanPublish(PublishProfile profile) => AwsProfile.IsLambda(profile);

    public bool RequiresPassword(PublishProfile profile) => false;

    public string? GetSetupError(PublishProfile profile) => AwsProfile.GetSetupError(profile, aws, ProfileProperties.PubwizAwsFunction);

    public IReadOnlyList<PublishStep> CreateSteps(PublishRequest request)
    {
        var profile = request.Profile;
        var location = AwsProfile.GetLocation(profile);
        var function = profile[ProfileProperties.PubwizAwsFunction]!;
        var customRuntime = string.Equals(profile[ProfileProperties.SelfContained], "true", StringComparison.OrdinalIgnoreCase);

        return
        [
            StagingPublish.CreateStep(dotnet, request),
            new PackageStep(StagingPublish.GetStagingDirectory(request) + ".zip", customRuntime),
            new UpdateCodeStep(aws, location, function),
            new SetDestinationStep($"Lambda function {function} ({location.Region})")
        ];
    }

    /// <summary>
    /// Zips the app. For a custom runtime the <c>bootstrap</c> executable must be there and executable on Linux,
    /// even when the zip is made on Windows.
    /// </summary>
    internal sealed class PackageStep(string zipPath, bool customRuntime) : PublishStep("Package")
    {
        public override Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
        {
            var folder = context.OutputDirectory ?? throw new InvalidOperationException("Nothing was published to package.");
            if (customRuntime && !File.Exists(Path.Combine(folder, BootstrapFileName)))
            {
                throw new PublishStepException(
                    $"A custom runtime starts a file named '{BootstrapFileName}', which the publish output doesn't have. "
                    + $"Add <AssemblyName>{BootstrapFileName}</AssemblyName> to the project, as the Lambda templates do.");
            }

            ZipPackager.CreateFromDirectory(folder, zipPath);
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update))
            {
                if (zip.GetEntry(BootstrapFileName) is { } bootstrap)
                {
                    ZipPackager.MarkExecutable(bootstrap);
                }
            }

            var size = new FileInfo(zipPath).Length;
            if (size > MaxDirectUploadBytes)
            {
                throw new PublishStepException($"The package is {size / 1024d / 1024d:0.0} MB; Lambda accepts up to 50 MB uploaded directly. Trim the app (e.g. PublishTrimmed) or deploy it as a container image.");
            }

            context.PackagePath = zipPath;
            context.Log.Info($"Created {zipPath} ({size / 1024d / 1024d:0.0} MB)");
            return Task.CompletedTask;
        }
    }

    /// <summary>Uploads the zip and waits until Lambda has finished updating the function.</summary>
    private sealed class UpdateCodeStep(AwsCli aws, AwsLocation location, string function) : PublishStep("Deploy to Lambda")
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

        public override async Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
        {
            try
            {
                // An update can't start while the previous one is still running.
                await WaitForUpdateAsync(context, failOnError: false, cancellationToken);

                context.ReportDetail($"uploading to {function}");
                await aws.UpdateLambdaCodeAsync(location, function, context.PackagePath!, cancellationToken);
                context.Log.Info($"Uploaded the code of {function}");

                await WaitForUpdateAsync(context, failOnError: true, cancellationToken);
                context.Log.Info($"{function} runs the new code");
            }
            catch (AwsCliException ex)
            {
                throw new PublishStepException(ex.Message, ex);
            }
        }

        /// <param name="context">The job.</param>
        /// <param name="failOnError">Fail when the last update failed (after uploading; before, an old failure doesn't matter).</param>
        /// <param name="cancellationToken">Cancels waiting.</param>
        private async Task WaitForUpdateAsync(PublishContext context, bool failOnError, CancellationToken cancellationToken)
        {
            var deadline = DateTimeOffset.UtcNow + Timeout;
            while (true)
            {
                var status = await aws.GetLambdaUpdateStatusAsync(location, function, cancellationToken);
                switch (status.Status)
                {
                    case "Failed" when failOnError:
                        throw new PublishStepException($"Lambda couldn't update {function}: {status.Reason ?? "no reason given"}");
                    case "InProgress" when DateTimeOffset.UtcNow < deadline:
                        context.ReportDetail("updating...");
                        await Task.Delay(PollInterval, cancellationToken);
                        continue;
                    case "InProgress":
                        throw new PublishStepException($"{function} is still updating after {Timeout.TotalMinutes:0} minutes.");
                    default:
                        return;
                }
            }
        }
    }
}
