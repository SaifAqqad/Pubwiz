using System.Text.Json.Serialization;

namespace Pubwiz.Core.Targets.Aws;

/// <summary>Where AWS commands go: a named AWS CLI profile (or the default credentials) and a region.</summary>
/// <param name="Profile">The AWS CLI profile from <c>~/.aws/config</c>, or <c>null</c> for the default credentials.</param>
/// <param name="Region">The region, e.g. <c>eu-west-1</c>.</param>
public sealed record AwsLocation(string? Profile, string Region)
{
    public override string ToString() => Profile is null ? Region : $"{Profile} · {Region}";
}

/// <summary>Who the AWS CLI is signed in as (<c>aws sts get-caller-identity</c>).</summary>
public sealed record AwsIdentity(
    [property: JsonPropertyName("Account")] string Account,
    [property: JsonPropertyName("Arn")] string Arn);

/// <summary>An Elastic Beanstalk environment.</summary>
/// <param name="ApplicationName">The application it belongs to.</param>
/// <param name="EnvironmentName">The environment, e.g. <c>shop-prod</c>.</param>
/// <param name="Url">The environment's domain name (CNAME), when it has one.</param>
/// <param name="Platform">The platform, e.g. <c>64bit Amazon Linux 2023 v3.2.0 running .NET 8</c>.</param>
/// <param name="Status">e.g. <c>Ready</c> or <c>Updating</c>.</param>
/// <param name="Health">e.g. <c>Green</c>.</param>
/// <param name="VersionLabel">The application version it runs.</param>
public sealed record BeanstalkEnvironment(
    [property: JsonPropertyName("application")] string ApplicationName,
    [property: JsonPropertyName("name")] string EnvironmentName,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("platform")] string? Platform,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("health")] string? Health,
    [property: JsonPropertyName("version")] string? VersionLabel)
{
    /// <summary>Windows Server platforms run the app in IIS and need a deployment manifest.</summary>
    public bool IsWindows => Platform?.Contains("Windows", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary><c>true</c> while a deployment or configuration change is in progress.</summary>
    public bool IsBusy => Status is "Launching" or "Updating";
}

/// <summary>An event of an Elastic Beanstalk environment, e.g. "Deploying new version to instance(s)."</summary>
public sealed record BeanstalkEvent(
    [property: JsonPropertyName("date")] DateTimeOffset Date,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("message")] string Message)
{
    public bool IsError => Severity is "ERROR" or "FATAL";
}

/// <summary>A Lambda function.</summary>
/// <param name="Name">The function name.</param>
/// <param name="Runtime">e.g. <c>dotnet8</c>, or <c>provided.al2023</c> for Native AOT and custom runtimes.</param>
/// <param name="Architectures"><c>x86_64</c> or <c>arm64</c>.</param>
/// <param name="PackageType"><c>Zip</c> or <c>Image</c>.</param>
public sealed record LambdaFunction(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("runtime")] string? Runtime,
    [property: JsonPropertyName("architectures")] IReadOnlyList<string>? Architectures,
    [property: JsonPropertyName("packageType")] string? PackageType)
{
    public bool IsArm64 => Architectures?.Contains("arm64") == true;

    /// <summary>Custom runtimes (<c>provided.*</c>) run a self-contained <c>bootstrap</c> executable, e.g. a Native AOT app.</summary>
    public bool IsCustomRuntime => Runtime?.StartsWith("provided", StringComparison.Ordinal) == true;

    /// <summary>The runtime identifier to publish for: Lambda runs Linux.</summary>
    public string RuntimeIdentifier => IsArm64 ? "linux-arm64" : "linux-x64";
}

/// <summary>The state of a Lambda function's last code update.</summary>
public sealed record LambdaUpdateStatus(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("reason")] string? Reason);

/// <summary>An Elastic Container Registry repository.</summary>
/// <param name="Name">e.g. <c>shop/api</c>.</param>
/// <param name="Uri">e.g. <c>123456789012.dkr.ecr.eu-west-1.amazonaws.com/shop/api</c>.</param>
public sealed record EcrRepository(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("uri")] string Uri)
{
    /// <summary>The registry host, e.g. <c>123456789012.dkr.ecr.eu-west-1.amazonaws.com</c>.</summary>
    public string Registry => Uri[..Uri.IndexOf('/')];
}

/// <summary>An ECS service.</summary>
/// <param name="Name">The service name.</param>
/// <param name="Cluster">The cluster's name.</param>
/// <param name="TaskDefinition">The ARN of the task definition it runs.</param>
/// <param name="LaunchType">e.g. <c>FARGATE</c>, or <c>null</c> with a capacity provider strategy.</param>
public sealed record EcsService(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("cluster")] string Cluster,
    [property: JsonPropertyName("taskDefinition")] string TaskDefinition,
    [property: JsonPropertyName("launchType")] string? LaunchType);

/// <summary>A deployment of an ECS service.</summary>
/// <param name="Status"><c>PRIMARY</c> for the newest one, <c>ACTIVE</c> for ones being replaced.</param>
/// <param name="TaskDefinition">The task definition it deploys.</param>
/// <param name="RolloutState"><c>IN_PROGRESS</c>, <c>COMPLETED</c> or <c>FAILED</c>; <c>null</c> for services that don't use the ECS deployment controller.</param>
public sealed record EcsDeployment(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("taskDefinition")] string TaskDefinition,
    [property: JsonPropertyName("rolloutState")] string? RolloutState,
    [property: JsonPropertyName("rolloutStateReason")] string? RolloutStateReason,
    [property: JsonPropertyName("runningCount")] int RunningCount,
    [property: JsonPropertyName("desiredCount")] int DesiredCount);

/// <summary>An ECS service event, e.g. "(service shop) has reached a steady state."</summary>
public sealed record EcsEvent(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("message")] string Message);

/// <summary>The deployments and latest events of an ECS service.</summary>
public sealed record EcsServiceStatus(
    [property: JsonPropertyName("deployments")] IReadOnlyList<EcsDeployment> Deployments,
    [property: JsonPropertyName("events")] IReadOnlyList<EcsEvent> Events);
