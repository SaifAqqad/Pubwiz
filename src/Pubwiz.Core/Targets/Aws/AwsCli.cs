using Pubwiz.Core.Infrastructure;
using Pubwiz.Core.Infrastructure.Platform;
using Pubwiz.Core.Infrastructure.Processes;
using System.Text.Json;

namespace Pubwiz.Core.Targets.Aws;

/// <summary>Thrown when an <c>aws</c> command fails.</summary>
public sealed class AwsCliException(string message) : Exception(message);

/// <summary>
/// Wrapper around the AWS CLI (<c>aws</c>), used for signing in, finding resources and deploying. It uses the
/// credentials the CLI already has (<c>aws configure</c>, <c>aws sso login</c>, environment variables, ...).
/// </summary>
/// <remarks>
/// Results are shaped with <c>--query</c> into small JSON objects, so the models don't depend on the casing of each
/// service's API (Elastic Beanstalk and Lambda use PascalCase, ECS and ECR camelCase).
/// </remarks>
/// <param name="processRunner">Runs aws.</param>
/// <param name="executablePath">The aws executable; found on <c>PATH</c> when <c>null</c>.</param>
public sealed class AwsCli(IProcessRunner processRunner, string? executablePath = null)
{
    public const string InstallDocsUrl = "https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html";

    /// <summary>The user name that goes with an ECR password (<c>aws ecr get-login-password</c>).</summary>
    public const string EcrUserName = "AWS";

    private Lazy<string?> _executablePath = new(() => executablePath ?? ExecutableLocator.FindInPath("aws"));

    public bool IsInstalled => _executablePath.Value is not null;

    /// <summary>Looks for aws again, e.g. right after installing it.</summary>
    /// <returns><c>true</c> when it's installed now.</returns>
    public bool Refresh()
    {
        _executablePath = new Lazy<string?>(() => ExecutableLocator.FindInPath("aws"));
        return IsInstalled;
    }

    // Sign-in and settings

    /// <summary>The profiles in <c>~/.aws/config</c> and <c>~/.aws/credentials</c>.</summary>
    public async Task<IReadOnlyList<string>> ListProfilesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["configure", "list-profiles"], cancellationToken);
        return result.Succeeded
            ? result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
    }

    /// <summary>The region set for a profile (or the default one), or <c>null</c> when none is.</summary>
    public async Task<string?> GetConfiguredRegionAsync(string? profile, CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["configure", "get", "region"];
        if (profile is not null)
        {
            arguments.AddRange(["--profile", profile]);
        }

        var result = await RunAsync(arguments, cancellationToken);
        return result.Succeeded && result.Output.Trim() is { Length: > 0 } region ? region : null;
    }

    /// <summary>Who the credentials belong to.</summary>
    /// <exception cref="AwsCliException">Not signed in (no credentials, or an expired SSO session).</exception>
    public async Task<AwsIdentity> GetIdentityAsync(AwsLocation location, CancellationToken cancellationToken = default) =>
        await QueryAsync<AwsIdentity>(location, ["sts", "get-caller-identity"], "aws sts get-caller-identity", cancellationToken);

    /// <summary>
    /// Runs an aws command attached to the terminal, for the ones that talk to the user: <c>aws configure</c>,
    /// <c>aws configure sso</c>, <c>aws sso login</c>.
    /// </summary>
    public async Task RunInteractiveAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        var exitCode = await processRunner.RunInteractiveAsync(CreateSpec(arguments), cancellationToken);
        if (exitCode != 0)
        {
            throw new AwsCliException($"aws {string.Join(' ', arguments)} failed with exit code {exitCode}");
        }
    }

    // Elastic Beanstalk

    public async Task<IReadOnlyList<BeanstalkEnvironment>> ListBeanstalkEnvironmentsAsync(AwsLocation location, CancellationToken cancellationToken = default)
    {
        const string query = "Environments[].{application:ApplicationName,name:EnvironmentName,url:CNAME,platform:SolutionStackName,status:Status,health:Health,version:VersionLabel}";
        var environments = await QueryAsync<List<BeanstalkEnvironment>>(location, ["elasticbeanstalk", "describe-environments", "--no-include-deleted", "--query", query],
            "aws elasticbeanstalk describe-environments", cancellationToken);
        return environments.OrderBy(e => e.ApplicationName, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.EnvironmentName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<BeanstalkEnvironment> GetBeanstalkEnvironmentAsync(AwsLocation location, string application, string environment, CancellationToken cancellationToken = default)
    {
        const string query = "Environments[0].{application:ApplicationName,name:EnvironmentName,url:CNAME,platform:SolutionStackName,status:Status,health:Health,version:VersionLabel}";
        var found = await QueryAsync<BeanstalkEnvironment?>(location,
            ["elasticbeanstalk", "describe-environments", "--application-name", application, "--environment-names", environment, "--no-include-deleted", "--query", query],
            "aws elasticbeanstalk describe-environments", cancellationToken);
        return found ?? throw new AwsCliException($"The environment '{environment}' of '{application}' wasn't found in {location.Region}.");
    }

    /// <summary>The environment's events since <paramref name="since"/>, oldest first.</summary>
    public async Task<IReadOnlyList<BeanstalkEvent>> GetBeanstalkEventsAsync(AwsLocation location, string application, string environment, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        const string query = "Events[].{date:EventDate,severity:Severity,message:Message}";
        var events = await QueryAsync<List<BeanstalkEvent>>(location,
            ["elasticbeanstalk", "describe-events", "--application-name", application, "--environment-name", environment, "--start-time", since.UtcDateTime.ToString("O"), "--query", query],
            "aws elasticbeanstalk describe-events", cancellationToken);
        return events.Where(e => e.Date > since).OrderBy(e => e.Date).ToList();
    }

    /// <summary>The S3 bucket Elastic Beanstalk keeps application versions in, created when it doesn't exist yet.</summary>
    public async Task<string> GetBeanstalkBucketAsync(AwsLocation location, CancellationToken cancellationToken = default) =>
        await QueryAsync<string>(location, ["elasticbeanstalk", "create-storage-location", "--query", "S3Bucket"], "aws elasticbeanstalk create-storage-location", cancellationToken);

    public async Task CreateBeanstalkVersionAsync(AwsLocation location, string application, string versionLabel, string bucket, string key, string description, CancellationToken cancellationToken = default)
    {
        await QueryAsync<JsonElement>(location,
            ["elasticbeanstalk", "create-application-version", "--application-name", application, "--version-label", versionLabel,
             "--description", description, "--source-bundle", $"S3Bucket={bucket},S3Key={key}", "--query", "ApplicationVersion.VersionLabel"],
            "aws elasticbeanstalk create-application-version", cancellationToken);
    }

    /// <summary>Starts deploying an application version to an environment; it then runs on AWS for a few minutes.</summary>
    public async Task DeployBeanstalkVersionAsync(AwsLocation location, string application, string environment, string versionLabel, CancellationToken cancellationToken = default)
    {
        await QueryAsync<JsonElement>(location,
            ["elasticbeanstalk", "update-environment", "--application-name", application, "--environment-name", environment, "--version-label", versionLabel, "--query", "Status"],
            "aws elasticbeanstalk update-environment", cancellationToken);
    }

    // S3

    /// <summary>Uploads a file to <c>s3://bucket/key</c>.</summary>
    public async Task UploadToS3Async(AwsLocation location, string filePath, string bucket, string key, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync([.. Arguments(location), "s3", "cp", filePath, $"s3://{bucket}/{key}", "--only-show-errors"], cancellationToken);
        EnsureSucceeded(result, "aws s3 cp");
    }

    // Lambda

    /// <summary>The Lambda functions deployed from a zip (functions that run a container image are left out).</summary>
    public async Task<IReadOnlyList<LambdaFunction>> ListLambdaFunctionsAsync(AwsLocation location, CancellationToken cancellationToken = default)
    {
        const string query = "Functions[].{name:FunctionName,runtime:Runtime,architectures:Architectures,packageType:PackageType}";
        var functions = await QueryAsync<List<LambdaFunction>>(location, ["lambda", "list-functions", "--query", query], "aws lambda list-functions", cancellationToken);
        return functions.Where(f => f.PackageType != "Image").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<LambdaFunction> GetLambdaFunctionAsync(AwsLocation location, string name, CancellationToken cancellationToken = default) =>
        await QueryAsync<LambdaFunction>(location,
            ["lambda", "get-function-configuration", "--function-name", name, "--query", "{name:FunctionName,runtime:Runtime,architectures:Architectures,packageType:PackageType}"],
            "aws lambda get-function-configuration", cancellationToken);

    /// <summary>Replaces the function's code with a zip (up to 50 MB; Lambda's limit for direct uploads).</summary>
    public async Task UpdateLambdaCodeAsync(AwsLocation location, string name, string zipPath, CancellationToken cancellationToken = default)
    {
        await QueryAsync<JsonElement>(location,
            ["lambda", "update-function-code", "--function-name", name, "--zip-file", $"fileb://{zipPath}", "--query", "LastUpdateStatus"],
            "aws lambda update-function-code", cancellationToken);
    }

    public async Task<LambdaUpdateStatus> GetLambdaUpdateStatusAsync(AwsLocation location, string name, CancellationToken cancellationToken = default) =>
        await QueryAsync<LambdaUpdateStatus>(location,
            ["lambda", "get-function-configuration", "--function-name", name, "--query", "{status:LastUpdateStatus,reason:LastUpdateStatusReason}"],
            "aws lambda get-function-configuration", cancellationToken);

    // ECR

    public async Task<IReadOnlyList<EcrRepository>> ListEcrRepositoriesAsync(AwsLocation location, CancellationToken cancellationToken = default)
    {
        var repositories = await QueryAsync<List<EcrRepository>>(location,
            ["ecr", "describe-repositories", "--query", "repositories[].{name:repositoryName,uri:repositoryUri}"], "aws ecr describe-repositories", cancellationToken);
        return repositories.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<EcrRepository> CreateEcrRepositoryAsync(AwsLocation location, string name, CancellationToken cancellationToken = default) =>
        await QueryAsync<EcrRepository>(location,
            ["ecr", "create-repository", "--repository-name", name, "--query", "repository.{name:repositoryName,uri:repositoryUri}"],
            "aws ecr create-repository", cancellationToken);

    /// <summary>A password for pushing to the account's ECR registries for 12 hours; the user name is <see cref="EcrUserName"/>.</summary>
    public async Task<string> GetEcrPasswordAsync(AwsLocation location, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync([.. Arguments(location), "ecr", "get-login-password"], cancellationToken);
        EnsureSucceeded(result, "aws ecr get-login-password");
        return result.Output.Trim();
    }

    // ECS

    /// <summary>The services of every cluster in the region.</summary>
    public async Task<IReadOnlyList<EcsService>> ListEcsServicesAsync(AwsLocation location, CancellationToken cancellationToken = default)
    {
        var clusters = await QueryAsync<List<string>>(location, ["ecs", "list-clusters", "--query", "clusterArns"], "aws ecs list-clusters", cancellationToken);
        var services = new List<EcsService>();
        foreach (var cluster in clusters)
        {
            var arns = await QueryAsync<List<string>>(location, ["ecs", "list-services", "--cluster", cluster, "--query", "serviceArns"], "aws ecs list-services", cancellationToken);

            // describe-services takes up to 10 services at a time.
            foreach (var batch in arns.Chunk(10))
            {
                const string query = "services[].{name:serviceName,cluster:clusterArn,taskDefinition:taskDefinition,launchType:launchType}";
                var described = await QueryAsync<List<EcsService>>(location, ["ecs", "describe-services", "--cluster", cluster, "--services", .. batch, "--query", query],
                    "aws ecs describe-services", cancellationToken);
                services.AddRange(described.Select(s => s with { Cluster = ResourceName(s.Cluster) }));
            }
        }

        return services.OrderBy(s => s.Cluster, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<string> GetEcsServiceTaskDefinitionAsync(AwsLocation location, string cluster, string service, CancellationToken cancellationToken = default) =>
        await QueryAsync<string>(location, ["ecs", "describe-services", "--cluster", cluster, "--services", service, "--query", "services[0].taskDefinition"],
            "aws ecs describe-services", cancellationToken)
        ?? throw new AwsCliException($"The service '{service}' wasn't found in the cluster '{cluster}'.");

    /// <summary>A task definition as JSON, in the shape <c>describe-task-definition</c> returns it.</summary>
    public async Task<string> GetEcsTaskDefinitionJsonAsync(AwsLocation location, string taskDefinition, CancellationToken cancellationToken = default) =>
        (await QueryAsync<JsonElement>(location, ["ecs", "describe-task-definition", "--task-definition", taskDefinition, "--query", "taskDefinition"],
            "aws ecs describe-task-definition", cancellationToken)).GetRawText();

    /// <summary>Registers a new revision of a task definition from <c>register-task-definition</c> input JSON.</summary>
    /// <returns>The new revision's ARN.</returns>
    public async Task<string> RegisterEcsTaskDefinitionAsync(AwsLocation location, string inputJson, CancellationToken cancellationToken = default)
    {
        // The JSON goes through a file: it can be longer than a command line allows.
        var inputFile = Path.Combine(Path.GetTempPath(), $"pubwiz-task-definition-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(inputFile, inputJson, cancellationToken);
        try
        {
            return await QueryAsync<string>(location,
                ["ecs", "register-task-definition", "--cli-input-json", $"file://{inputFile}", "--query", "taskDefinition.taskDefinitionArn"],
                "aws ecs register-task-definition", cancellationToken);
        }
        finally
        {
            File.Delete(inputFile);
        }
    }

    /// <summary>Starts a deployment of the service with a task definition.</summary>
    public async Task UpdateEcsServiceAsync(AwsLocation location, string cluster, string service, string taskDefinition, CancellationToken cancellationToken = default)
    {
        await QueryAsync<JsonElement>(location,
            ["ecs", "update-service", "--cluster", cluster, "--service", service, "--task-definition", taskDefinition, "--force-new-deployment", "--query", "service.status"],
            "aws ecs update-service", cancellationToken);
    }

    public async Task<EcsServiceStatus> GetEcsServiceStatusAsync(AwsLocation location, string cluster, string service, CancellationToken cancellationToken = default)
    {
        const string query = "services[0].{deployments:deployments[].{status:status,taskDefinition:taskDefinition,rolloutState:rolloutState,"
                             + "rolloutStateReason:rolloutStateReason,runningCount:runningCount,desiredCount:desiredCount},events:events[:10].{id:id,message:message}}";
        return await QueryAsync<EcsServiceStatus>(location, ["ecs", "describe-services", "--cluster", cluster, "--services", service, "--query", query],
            "aws ecs describe-services", cancellationToken);
    }

    /// <summary>The name at the end of an ARN, e.g. <c>prod</c> for <c>arn:aws:ecs:eu-west-1:123:cluster/prod</c>.</summary>
    public static string ResourceName(string arn) => arn[(arn.LastIndexOf('/') + 1)..];

    private async Task<T> QueryAsync<T>(AwsLocation location, IReadOnlyList<string> arguments, string command, CancellationToken cancellationToken)
    {
        var result = await RunAsync([.. Arguments(location), .. arguments, "--output", "json"], cancellationToken);
        EnsureSucceeded(result, command);
        return Deserialize<T>(result.Output)!;
    }

    private static IEnumerable<string> Arguments(AwsLocation location) =>
        location.Profile is null ? ["--region", location.Region] : ["--region", location.Region, "--profile", location.Profile];

    private Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        processRunner.RunAsync(CreateSpec(arguments), onOutput: null, cancellationToken);

    private ProcessSpec CreateSpec(IReadOnlyList<string> arguments)
    {
        var executable = _executablePath.Value ?? throw new PrerequisiteMissingException("AWS CLI (aws)", $"Install it: {InstallDocsUrl}");

        // Without this, aws v2 pipes long output through a pager (less) and waits for a key press.
        return new ProcessSpec(executable, arguments)
        {
            Environment = new Dictionary<string, string?> { ["AWS_PAGER"] = string.Empty, ["AWS_CLI_AUTO_PROMPT"] = "off" }
        };
    }

    private static void EnsureSucceeded(ProcessResult result, string command)
    {
        if (!result.Succeeded)
        {
            throw new AwsCliException(DescribeError(result.Error) ?? $"{command} failed with exit code {result.ExitCode}");
        }
    }

    /// <summary>
    /// The useful part of an aws error, e.g. "An error occurred (AccessDenied) when calling the ListFunctions operation: ..."
    /// without the blank lines around it.
    /// </summary>
    internal static string? DescribeError(string error)
    {
        var lines = error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? null : string.Join(' ', lines);
    }

    internal static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json);
}
