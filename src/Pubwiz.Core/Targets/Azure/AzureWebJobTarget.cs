using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Publishing.Steps;
using Pubwiz.Core.Targets.Azure.Kudu;
using System.Text.Json;

namespace Pubwiz.Core.Targets.Azure;

/// <summary>
/// Deploys a console app or worker as an App Service WebJob (profiles with <c>WebJobName</c> set).
/// </summary>
public sealed class AzureWebJobTarget(DotnetCli dotnet, KuduClient kudu, KuduCredentialsProvider credentials) : IPublishTarget
{
    public PublishTargetType Type => PublishTargetType.AzureWebJob;

    public bool CanPublish(PublishProfile profile) => AzureProfile.IsWebJob(profile);

    public bool RequiresPassword(PublishProfile profile) => !AzureProfile.UsesAzCli(profile);

    public TargetPlatform GetPlatform(PublishProfile profile) => AzureProfile.GetPlatform(profile);

    public IReadOnlyList<PublishStep> CreateSteps(PublishRequest request)
    {
        var profile = request.Profile;
        var zipPath = StagingPublish.GetStagingDirectory(request) + ".zip";
        var isLinux = string.Equals(profile[ProfileProperties.IsLinux] ?? profile[ProfileProperties.PubwizAzureLinux], "true", StringComparison.OrdinalIgnoreCase);

        return
        [
            StagingPublish.CreateStep(dotnet, request),
            new PrepareWebJobStep(isLinux, AzureProfile.IsTriggeredWebJob(profile) ? profile[ProfileProperties.PubwizWebJobSchedule] : null),
            new PackageZipStep(zipPath),
            new UploadWebJobStep(kudu, credentials, AzureProfile.GetScmUrl(profile), profile[ProfileProperties.WebJobName]!, AzureProfile.IsTriggeredWebJob(profile)),
            new SetDestinationStep($"{AzureProfile.GetSiteUrl(profile)} (WebJob {profile[ProfileProperties.WebJobName]})")
        ];
    }

    /// <summary>
    /// Adds what Kudu needs to start the job: a run script (unless the app ships its own) and,
    /// for scheduled triggered jobs, a settings.job file.
    /// </summary>
    internal sealed class PrepareWebJobStep(bool isLinux, string? schedule) : PublishStep("Prepare WebJob")
    {
        public override Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
        {
            var folder = context.OutputDirectory!;
            var scriptName = isLinux ? "run.sh" : "run.cmd";
            var scriptPath = Path.Combine(folder, scriptName);

            if (!File.Exists(scriptPath))
            {
                var dllName = FindEntryAssembly(folder);
                var script = isLinux ? $"#!/bin/bash\ndotnet {dllName}\n" : $"dotnet {dllName}\r\n";
                File.WriteAllText(scriptPath, script);
                context.Log.Info($"Added {scriptName}: dotnet {dllName}");
            }

            if (schedule is not null)
            {
                File.WriteAllText(Path.Combine(folder, "settings.job"), JsonSerializer.Serialize(new { schedule }));
                context.Log.Info($"Added settings.job with schedule '{schedule}'");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// The app's dll: the one with a matching <c>.runtimeconfig.json</c> (only the entry assembly has one),
        /// which is right even when AssemblyName differs from the project name.
        /// </summary>
        internal static string FindEntryAssembly(string folder)
        {
            var runtimeConfig = Directory.GetFiles(folder, "*.runtimeconfig.json").FirstOrDefault()
                ?? throw new PublishStepException("The publish folder has no .runtimeconfig.json, so the app's dll can't be found. Is this an executable project?");

            return Path.GetFileName(runtimeConfig).Replace(".runtimeconfig.json", ".dll", StringComparison.Ordinal);
        }
    }

    private sealed class UploadWebJobStep(KuduClient kudu, KuduCredentialsProvider credentialsProvider, Uri scmUrl, string name, bool triggered)
        : PublishStep("Deploy WebJob")
    {
        public override async Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
        {
            var credentials = await credentialsProvider.GetAsync(context, cancellationToken);

            context.ReportDetail($"uploading {name} to {scmUrl.Host}");
            try
            {
                await kudu.DeployWebJobAsync(scmUrl, name, triggered, context.PackagePath!, credentials, context.ReportProgress, cancellationToken);
                context.Log.Info($"{(triggered ? "Triggered" : "Continuous")} WebJob '{name}' deployed");
            }
            catch (KuduException ex)
            {
                throw new PublishStepException(ex.Message, ex);
            }
        }
    }
}
