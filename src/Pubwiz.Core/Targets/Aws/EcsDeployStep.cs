using Pubwiz.Core.Publishing;

namespace Pubwiz.Core.Targets.Aws;

/// <summary>
/// Deploys a pushed image to an ECS service: registers a new revision of the service's task definition with the
/// image, updates the service to it, and waits until ECS has replaced the running tasks.
/// </summary>
/// <param name="aws">The AWS CLI.</param>
/// <param name="location">The AWS profile and region.</param>
/// <param name="cluster">The cluster name.</param>
/// <param name="service">The service name.</param>
/// <param name="image">The image to run, e.g. <c>123.dkr.ecr.eu-west-1.amazonaws.com/shop:latest</c>.</param>
/// <param name="containerName">The container to change, when the task definition has several.</param>
/// <param name="pollInterval">How often to check on the deployment (10 seconds by default; shorter in tests).</param>
internal sealed class EcsDeployStep(AwsCli aws, AwsLocation location, string cluster, string service, string image, string? containerName, TimeSpan? pollInterval = null)
    : PublishStep("Deploy to ECS")
{    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

    public override async Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
    {
        try
        {
            context.ReportDetail("reading the task definition");
            var current = await aws.GetEcsServiceTaskDefinitionAsync(location, cluster, service, cancellationToken);
            var described = await aws.GetEcsTaskDefinitionJsonAsync(location, current, cancellationToken);

            string input, container;
            try
            {
                (input, container) = EcsTaskDefinition.WithImage(described, image, containerName);
            }
            catch (ArgumentException ex)
            {
                throw new PublishStepException(ex.Message, ex);
            }

            var revision = await aws.RegisterEcsTaskDefinitionAsync(location, input, cancellationToken);
            context.Log.Info($"Registered {AwsCli.ResourceName(revision)} ({container} runs {image})");

            // Events from before the update aren't shown again.
            var seenEvents = (await aws.GetEcsServiceStatusAsync(location, cluster, service, cancellationToken)).Events.Select(e => e.Id).ToHashSet();
            await aws.UpdateEcsServiceAsync(location, cluster, service, revision, cancellationToken);
            context.Log.Info($"Deploying {AwsCli.ResourceName(revision)} to {cluster}/{service}");

            await WaitForRolloutAsync(context, revision, seenEvents, cancellationToken);
        }
        catch (AwsCliException ex)
        {
            throw new PublishStepException(ex.Message, ex);
        }
    }

    private async Task WaitForRolloutAsync(PublishContext context, string revision, HashSet<string> seenEvents, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + Timeout;
        while (true)
        {
            await Task.Delay(pollInterval ?? TimeSpan.FromSeconds(10), cancellationToken);
            var status = await aws.GetEcsServiceStatusAsync(location, cluster, service, cancellationToken);

            // Events come newest first.
            foreach (var item in status.Events.Where(e => seenEvents.Add(e.Id)).Reverse())
            {
                context.Log.Write(LogSource.Output, item.Message);
            }

            var deployment = status.Deployments.FirstOrDefault(d => d.TaskDefinition == revision)
                ?? throw new PublishStepException($"The deployment of {AwsCli.ResourceName(revision)} disappeared; another deployment may have replaced it.");

            context.ReportDetail($"{deployment.RunningCount}/{deployment.DesiredCount} tasks running");
            if (deployment.RolloutState == "FAILED")
            {
                throw new PublishStepException($"ECS rolled the deployment back: {deployment.RolloutStateReason ?? "no reason given"}");
            }

            // Services without the ECS deployment controller have no rollout state: done when only the new tasks run.
            var done = deployment.RolloutState == "COMPLETED"
                       || (deployment.RolloutState is null && status.Deployments.Count == 1 && deployment.RunningCount == deployment.DesiredCount);
            if (done)
            {
                context.Log.Info($"{service} runs {AwsCli.ResourceName(revision)} ({deployment.RunningCount} task(s))");
                return;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new PublishStepException($"{service} is still deploying after {Timeout.TotalMinutes:0} minutes. See its events in the AWS console.");
            }
        }
    }
}
