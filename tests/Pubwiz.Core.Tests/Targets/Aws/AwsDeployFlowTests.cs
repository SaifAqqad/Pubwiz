using Pubwiz.Core.Discovery;
using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Targets;
using Pubwiz.Core.Targets.Aws;
using Pubwiz.Core.Tests.TestHelpers;
using System.Text.Json;

namespace Pubwiz.Core.Tests.Targets.Aws;

/// <summary>
/// Runs the Elastic Beanstalk and ECS deployments against a scripted AWS CLI, which answers the way AWS does
/// while a deployment runs (busy, then done, with events along the way).
/// </summary>
public class AwsDeployFlowTests
{
    private static readonly ProjectInfo Project = new() { Name = "Shop", FilePath = "/src/Shop/Shop.csproj", Kind = ProjectKind.Web };

    private static readonly TimeSpan NoWait = TimeSpan.FromMilliseconds(1);

    // Elastic Beanstalk

    [Fact]
    public async Task ElasticBeanstalk_UploadsAVersionAndWaitsForIt()
    {
        var environment = new ScriptedBeanstalk(failDeployment: false);
        var job = await RunBeanstalkAsync(environment);

        Assert.Null(job.Error);
        Assert.Contains(environment.Runner.Runs, run => run.Contains("cp") && run.Contains("s3://elasticbeanstalk-eu-west-1-123/shop/" + environment.DeployedLabel + ".zip"));
        Assert.Contains(environment.Runner.Runs, run => run.Contains("create-application-version") && run.Contains("S3Bucket=elasticbeanstalk-eu-west-1-123,S3Key=shop/" + environment.DeployedLabel + ".zip"));
        Assert.Contains(job.Log.Snapshot(), line => line.Text == "INFO: Environment update completed successfully.");
    }

    [Fact]
    public async Task ElasticBeanstalk_FailsOnErrorEvents()
    {
        var job = await RunBeanstalkAsync(new ScriptedBeanstalk(failDeployment: true));

        Assert.Equal("The deployment failed: Failed to deploy application.", job.Error);
    }

    [Fact]
    public async Task ElasticBeanstalk_DoesntStartWhileTheEnvironmentIsBusy()
    {
        var job = await RunBeanstalkAsync(new ScriptedBeanstalk(failDeployment: false) { Status = "Updating" });

        Assert.Equal("shop-prod is busy (Updating). Try again when it's Ready.", job.Error);
    }

    private static async Task<PublishJob> RunBeanstalkAsync(ScriptedBeanstalk environment)
    {
        var profile = new PublishProfile("Prod");
        profile[ProfileProperties.WebPublishMethod] = "Custom";
        profile[ProfileProperties.PublishProvider] = ProfileProperties.AwsElasticBeanstalkProvider;
        profile[ProfileProperties.PubwizAwsRegion] = "eu-west-1";
        profile[ProfileProperties.PubwizAwsApplication] = "shop";
        profile[ProfileProperties.PubwizAwsEnvironment] = "shop-prod";

        var target = new ElasticBeanstalkTarget(new DotnetCli(environment.Runner, "dotnet"), new AwsCli(environment.Runner, "aws"), NoWait);
        var steps = target.CreateSteps(new PublishRequest(Project, profile));

        // Only the upload and deploy steps: the package is a placeholder.
        return await RunStepsAsync(profile, steps.Where(s => s.Name is "Upload version" or "Deploy to Elastic Beanstalk"), packagePath: "/tmp/bundle.zip");
    }

    /// <summary>An environment that is Ready, runs a new version once it's deployed, and reports events.</summary>
    private sealed class ScriptedBeanstalk
    {
        private int _pollsSinceDeploy = -1;

        public ScriptedBeanstalk(bool failDeployment)
        {
            Runner = new ScriptedProcessRunner(arguments =>
            {
                string Value(string name) => arguments[arguments.ToList().IndexOf(name) + 1];

                switch (Command(arguments))
                {
                    case "describe-environments":
                        var deploying = _pollsSinceDeploy >= 0 && _pollsSinceDeploy++ < 1;
                        var version = DeployedLabel is not null && !failDeployment && !deploying ? DeployedLabel : "old";
                        return Json(new { application = "shop", name = "shop-prod", url = "shop.eu-west-1.elasticbeanstalk.com", status = deploying ? "Updating" : Status, version });
                    case "create-storage-location":
                        return "\"elasticbeanstalk-eu-west-1-123\"";
                    case "update-environment":
                        DeployedLabel = Value("--version-label");
                        _pollsSinceDeploy = 0;
                        Status = "Ready";
                        return "\"Updating\"";
                    case "describe-events" when _pollsSinceDeploy >= 0:
                        return Json(new[]
                        {
                            new { date = DateTimeOffset.UtcNow, severity = failDeployment ? "ERROR" : "INFO", message = failDeployment ? "Failed to deploy application." : "Environment update completed successfully." }
                        });
                    case "describe-events":
                        return "[]";
                    default:
                        return "\"ok\"";
                }
            });
        }

        public ScriptedProcessRunner Runner { get; }

        public string Status { get; set; } = "Ready";

        public string? DeployedLabel { get; private set; }
    }

    // ECS

    [Fact]
    public async Task Ecs_RegistersARevisionAndWaitsForTheRollout()
    {
        var service = new ScriptedEcs(rollout: "COMPLETED");
        var job = await RunEcsAsync(service);

        Assert.Null(job.Error);
        var registered = JsonDocument.Parse(service.RegisteredJson!).RootElement;
        Assert.Equal("123.dkr.ecr.eu-west-1.amazonaws.com/shop:v2", registered.GetProperty("containerDefinitions")[0].GetProperty("image").GetString());
        Assert.Contains(service.Runner.Runs, run => run.Contains("update-service") && run.Contains("arn:aws:ecs:eu-west-1:123:task-definition/shop:8"));
        Assert.Contains(job.Log.Snapshot(), line => line.Text == "(service shop) has started 1 tasks.");
        Assert.DoesNotContain(job.Log.Snapshot(), line => line.Text == "(service shop) has reached a steady state.");
    }

    [Fact]
    public async Task Ecs_ReportsARolledBackDeployment()
    {
        var job = await RunEcsAsync(new ScriptedEcs(rollout: "FAILED"));

        Assert.Equal("ECS rolled the deployment back: tasks failed to start", job.Error);
    }

    private static async Task<PublishJob> RunEcsAsync(ScriptedEcs service)
    {
        var step = new EcsDeployStep(new AwsCli(service.Runner, "aws"), new AwsLocation(null, "eu-west-1"), "prod", "shop",
            "123.dkr.ecr.eu-west-1.amazonaws.com/shop:v2", containerName: null, NoWait);
        return await RunStepsAsync(new PublishProfile("Ecs"), [step], packagePath: null);
    }

    /// <summary>A service running revision 7; once updated, its new deployment rolls out on the second check.</summary>
    private sealed class ScriptedEcs
    {
        private const string Revision = "arn:aws:ecs:eu-west-1:123:task-definition/shop:8";
        private bool _updated;
        private int _checks;

        public ScriptedEcs(string rollout)
        {
            Runner = new ScriptedProcessRunner(arguments =>
            {
                var query = arguments[arguments.ToList().IndexOf("--query") + 1];
                switch (Command(arguments))
                {
                    case "describe-services" when query == "services[0].taskDefinition":
                        return "\"arn:aws:ecs:eu-west-1:123:task-definition/shop:7\"";
                    case "describe-services" when !_updated:
                        return Json(new { deployments = new[] { Deployment("arn:aws:ecs:eu-west-1:123:task-definition/shop:7", "COMPLETED", 1) }, events = new[] { new { id = "e1", message = "(service shop) has reached a steady state." } } });
                    case "describe-services":
                        var state = _checks++ == 0 ? "IN_PROGRESS" : rollout;
                        return Json(new
                        {
                            deployments = new[] { Deployment(Revision, state, state == "COMPLETED" ? 1 : 0) },
                            events = new[] { new { id = "e2", message = "(service shop) has started 1 tasks." }, new { id = "e1", message = "(service shop) has reached a steady state." } }
                        });
                    case "describe-task-definition":
                        return """{"family":"shop","revision":7,"status":"ACTIVE","containerDefinitions":[{"name":"web","image":"123.dkr.ecr.eu-west-1.amazonaws.com/shop:v1"}]}""";
                    case "register-task-definition":
                        RegisteredJson = File.ReadAllText(arguments[arguments.ToList().IndexOf("--cli-input-json") + 1]["file://".Length..]);
                        return $"\"{Revision}\"";
                    case "update-service":
                        _updated = true;
                        return "\"ACTIVE\"";
                    default:
                        throw new ScriptedProcessRunner.ScriptedFailure($"unexpected: {string.Join(' ', arguments)}");
                }
            });
        }

        public ScriptedProcessRunner Runner { get; }

        public string? RegisteredJson { get; private set; }

        private static object Deployment(string taskDefinition, string rolloutState, int running) => new
        {
            status = "PRIMARY",
            taskDefinition,
            rolloutState,
            rolloutStateReason = rolloutState == "FAILED" ? "tasks failed to start" : null,
            runningCount = running,
            desiredCount = 1
        };
    }

    // Shared

    /// <summary>The aws subcommand, e.g. <c>describe-environments</c> (after the region and service name).</summary>
    private static string Command(IReadOnlyList<string> arguments) => arguments.SkipWhile(a => a.StartsWith("--") || a == "eu-west-1").Skip(1).First();

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static async Task<PublishJob> RunStepsAsync(PublishProfile profile, IEnumerable<PublishStep> steps, string? packagePath)
    {
        var stepList = steps.ToList();
        var job = new PublishJob(new PublishRequest(Project, profile), PublishTargetType.AwsEcs, stepList, setupError: null);
        var context = new PublishContext(job) { PackagePath = packagePath };
        try
        {
            foreach (var step in stepList)
            {
                await step.ExecuteAsync(context, TestContext.Current.CancellationToken);
            }
        }
        catch (PublishStepException ex)
        {
            job.Error = ex.Message;
        }

        return job;
    }
}
