using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets.Aws;
using Pubwiz.Tool.Aws;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Creates Elastic Beanstalk and Lambda profiles by picking an existing environment or function. Profiles keep the
/// AWS CLI profile and region, and sign in through the AWS CLI, so no secret is stored.
/// </summary>
public sealed class AwsProfilePage(IAnsiConsole console, AwsSession session, AwsCli aws)
{
    public async Task<PublishProfile> CreateElasticBeanstalkAsync(ProjectInfo project, CancellationToken cancellationToken)
    {
        var location = await session.EnsureReadyAsync(cancellationToken);
        var environment = await console.ChooseWithRefreshAsync("Elastic Beanstalk environment", "Loading environments...",
            token => aws.ListBeanstalkEnvironmentsAsync(location, token),
            e => [e.EnvironmentName, e.ApplicationName, e.Platform ?? string.Empty, e.Health ?? e.Status],
            emptyMessage: $"No Elastic Beanstalk environments were found in {location.Region}. Create one (e.g. in the AWS console), then refresh.",
            cancellationToken);

        var profile = CreateProfile($"{environment.EnvironmentName} - Elastic Beanstalk", ProfileProperties.AwsElasticBeanstalkProvider, location);
        profile[ProfileProperties.PubwizAwsApplication] = environment.ApplicationName;
        profile[ProfileProperties.PubwizAwsEnvironment] = environment.EnvironmentName;
        profile[ProfileProperties.PubwizAwsWindows] = environment.IsWindows ? "true" : null;
        profile[ProfileProperties.SiteUrlToLaunchAfterPublish] = string.IsNullOrEmpty(environment.Url) ? null : $"http://{environment.Url}";

        // The Linux platform runs the .NET versions it has installed; a self-contained app brings its own.
        if (!environment.IsWindows && console.AskYesNo("Publish self-contained?", defaultValue: false, hint: "when the platform doesn't have the app's .NET version"))
        {
            profile[ProfileProperties.RuntimeIdentifier] = "linux-x64";
            profile[ProfileProperties.SelfContained] = "true";
        }

        AskBuildSettings(project, profile);
        return profile;
    }

    public async Task<PublishProfile> CreateLambdaAsync(ProjectInfo project, CancellationToken cancellationToken)
    {
        var location = await session.EnsureReadyAsync(cancellationToken);
        var function = await console.ChooseWithRefreshAsync("Lambda function", "Loading functions...",
            token => aws.ListLambdaFunctionsAsync(location, token),
            f => [f.Name, f.Runtime ?? string.Empty, f.IsArm64 ? "arm64" : "x86_64"],
            emptyMessage: $"No Lambda functions deployed from a zip were found in {location.Region}. Create one (e.g. with the AWS console or dotnet lambda deploy-function), then refresh.",
            cancellationToken);

        var profile = CreateProfile($"{function.Name} - Lambda", ProfileProperties.AwsLambdaProvider, location);
        profile[ProfileProperties.PubwizAwsFunction] = function.Name;

        // Lambda runs Linux on the function's architecture. Custom runtimes (Native AOT) start a self-contained bootstrap.
        profile[ProfileProperties.RuntimeIdentifier] = function.RuntimeIdentifier;
        profile[ProfileProperties.SelfContained] = function.IsCustomRuntime ? "true" : "false";
        if (function.IsCustomRuntime)
        {
            console.MarkupLine($"[dim]{function.Runtime.EscapeMarkup()} runs a self-contained executable named [bold]{LambdaTarget.BootstrapFileName}[/], as the Native AOT template builds it.[/]");
        }

        AskBuildSettings(project, profile);
        return profile;
    }

    private static PublishProfile CreateProfile(string name, string provider, AwsLocation location)
    {
        var profile = new PublishProfile(name);
        profile[ProfileProperties.WebPublishMethod] = "Custom";
        profile[ProfileProperties.PublishProvider] = provider;
        profile[ProfileProperties.PubwizAwsProfile] = location.Profile;
        profile[ProfileProperties.PubwizAwsRegion] = location.Region;
        return profile;
    }

    private void AskBuildSettings(ProjectInfo project, PublishProfile profile)
    {
        var targetFramework = CommonPrompts.ChooseTargetFramework(console, project);
        profile[ProfileProperties.LastUsedBuildConfiguration] = CommonPrompts.ChooseConfiguration(console);
        profile[ProfileProperties.LastUsedPlatform] = "Any CPU";
        profile[ProfileProperties.TargetFramework] = project.TargetFrameworks.Count > 1 ? targetFramework : null;
    }
}
