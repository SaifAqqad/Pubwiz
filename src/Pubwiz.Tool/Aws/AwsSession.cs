using Pubwiz.Core;
using Pubwiz.Core.Infrastructure;
using Pubwiz.Core.Infrastructure.Platform;
using Pubwiz.Core.Infrastructure.Processes;
using Pubwiz.Core.Targets.Aws;

namespace Pubwiz.Tool.Aws;

/// <summary>
/// Gets the AWS CLI ready: installed (offering an install command), an AWS CLI profile and region chosen, and signed in
/// (running <c>aws configure sso</c>, <c>aws sso login</c> or <c>aws configure</c> when needed).
/// </summary>
public sealed class AwsSession(IAnsiConsole console, AwsCli aws, IProcessRunner processRunner)
{
    /// <summary>The regions offered when the AWS CLI profile has none (the most used ones first).</summary>
    private static readonly string[] Regions =
    [
        "us-east-1", "us-east-2", "us-west-1", "us-west-2", "eu-west-1", "eu-west-2", "eu-west-3", "eu-central-1", "eu-central-2",
        "eu-north-1", "eu-south-1", "eu-south-2", "ca-central-1", "ca-west-1", "sa-east-1", "me-central-1", "me-south-1", "il-central-1",
        "af-south-1", "ap-south-1", "ap-south-2", "ap-southeast-1", "ap-southeast-2", "ap-southeast-3", "ap-southeast-4", "ap-southeast-5",
        "ap-southeast-7", "ap-northeast-1", "ap-northeast-2", "ap-northeast-3", "ap-east-1", "mx-central-1"
    ];

    private const string DefaultCredentials = "Default credentials";

    private AwsLocation? _location;

    /// <summary>Makes sure aws is installed and signed in, and picks the AWS CLI profile and region (once per run).</summary>
    public async Task<AwsLocation> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (_location is not null)
        {
            return _location;
        }

        await EnsureInstalledAsync(cancellationToken);
        var profile = await ChooseProfileAsync(cancellationToken);
        var region = await ChooseRegionAsync(profile, cancellationToken);
        var location = new AwsLocation(profile, region);

        var identity = await SignInAsync(location, cancellationToken);
        console.MarkupLine($"[dim]AWS: {identity.Arn.EscapeMarkup()} · {location.ToString().EscapeMarkup()}[/]");
        return _location = location;
    }

    private async Task EnsureInstalledAsync(CancellationToken cancellationToken)
    {
        if (aws.IsInstalled)
        {
            return;
        }

        console.MarkupLine("The AWS CLI ([bold]aws[/]) is needed to find your resources and sign in.");
        if (GetInstallCommand() is not { } command || !AskToRun(command))
        {
            throw new PrerequisiteMissingException("AWS CLI (aws)", $"Install it ({AwsCli.InstallDocsUrl}), then run this again.");
        }

        var exitCode = await Shell.RunInteractiveAsync(processRunner, command, cancellationToken);
        if (exitCode != 0 || !aws.Refresh())
        {
            throw new PrerequisiteMissingException("AWS CLI (aws)", $"The install command didn't finish (exit code {exitCode}). See {AwsCli.InstallDocsUrl}.");
        }

        console.MarkupLine($"{Widgets.SuccessCheckMarkup} AWS CLI installed");
    }

    /// <summary>winget on Windows, Homebrew on macOS and Linux (where AWS ships no distribution packages).</summary>
    private static string? GetInstallCommand()
    {
        if (OperatingSystemInfo.IsWindows)
        {
            return "winget install --exact --id Amazon.AWSCLI";
        }

        return ExecutableLocator.FindInPath("brew") is not null ? "brew install awscli" : null;
    }

    private bool AskToRun(string command)
    {
        console.MarkupLine($"  [{Widgets.PrimaryColorMarkup}]{command.EscapeMarkup()}[/]");
        return console.AskYesNo("Run this command now?", defaultValue: false);
    }

    /// <returns>The AWS CLI profile, or <c>null</c> for the default credentials.</returns>
    private async Task<string?> ChooseProfileAsync(CancellationToken cancellationToken)
    {
        var profiles = await aws.ListProfilesAsync(cancellationToken);
        if (profiles.Count == 0)
        {
            // Environment variables (AWS_ACCESS_KEY_ID, AWS_PROFILE, ...) work without any profile.
            if (Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID") is not null || Environment.GetEnvironmentVariable("AWS_PROFILE") is not null)
            {
                return null;
            }

            await SetUpProfileAsync(cancellationToken);
            profiles = await aws.ListProfilesAsync(cancellationToken);
        }

        if (profiles.Count == 1)
        {
            return profiles[0];
        }

        var choices = profiles.Count == 0 ? [DefaultCredentials] : profiles.OrderByDescending(p => p == "default").ToList();
        var chosen = console.Choose("AWS CLI profile", choices);
        return chosen == DefaultCredentials ? null : chosen;
    }

    /// <summary>Runs the AWS CLI's own setup for users who haven't signed in on this machine before.</summary>
    private async Task SetUpProfileAsync(CancellationToken cancellationToken)
    {
        const string sso = "Sign in with IAM Identity Center (SSO) [dim]· aws configure sso[/]";
        const string keys = "Enter an access key [dim]· aws configure[/]";

        console.MarkupLine("The AWS CLI has no sign-in set up on this machine yet.");
        var useSso = console.Choose("Set up", [sso, keys], text => text) == sso;
        await RunAsync(useSso ? ["configure", "sso"] : ["configure"], cancellationToken);
    }

    private async Task<string> ChooseRegionAsync(string? profile, CancellationToken cancellationToken)
    {
        // The AWS CLI reads the region from these variables before the profile's config.
        var configured = Environment.GetEnvironmentVariable("AWS_REGION")
                         ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
                         ?? await aws.GetConfiguredRegionAsync(profile, cancellationToken);
        var regions = configured is null ? Regions : Regions.Where(r => r != configured).Prepend(configured);
        return console.Choose("AWS region", regions);
    }

    /// <summary>Checks the credentials, and offers to sign in again when they're missing or expired.</summary>
    private async Task<AwsIdentity> SignInAsync(AwsLocation location, CancellationToken cancellationToken)
    {
        try
        {
            return await console.Status().StartAsync("Checking AWS credentials...", _ => aws.GetIdentityAsync(location, cancellationToken));
        }
        catch (AwsCliException ex)
        {
            console.Write(Widgets.Warning(ex.Message));
            console.WriteLine();
        }

        List<string> profileArguments = location.Profile is null ? [] : ["--profile", location.Profile];
        const string ssoLogin = "Sign in again with SSO [dim]· aws sso login[/]";
        const string configure = "Enter an access key [dim]· aws configure[/]";

        var useSso = console.Choose("Sign in", [ssoLogin, configure], text => text) == ssoLogin;
        await RunAsync(useSso ? ["sso", "login", .. profileArguments] : ["configure", .. profileArguments], cancellationToken);

        try
        {
            return await aws.GetIdentityAsync(location, cancellationToken);
        }
        catch (AwsCliException ex)
        {
            throw new UserErrorException($"Signing in to AWS didn't work: {ex.Message}");
        }
    }

    private async Task RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            await aws.RunInteractiveAsync(arguments, cancellationToken);
        }
        catch (AwsCliException ex)
        {
            throw new UserErrorException(ex.Message);
        }
    }
}
