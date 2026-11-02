using Pubwiz.Core.Profiles;

namespace Pubwiz.Core.Targets.Aws;

/// <summary>
/// Reads the AWS settings of a profile.
/// </summary>
public static class AwsProfile
{
    public static bool IsElasticBeanstalk(PublishProfile profile) => IsCustom(profile, ProfileProperties.AwsElasticBeanstalkProvider);

    public static bool IsLambda(PublishProfile profile) => IsCustom(profile, ProfileProperties.AwsLambdaProvider);

    /// <summary>The AWS CLI profile and region, or <c>null</c> when the profile has no region.</summary>
    public static AwsLocation? TryGetLocation(PublishProfile profile) =>
        profile[ProfileProperties.PubwizAwsRegion] is { } region ? new AwsLocation(profile[ProfileProperties.PubwizAwsProfile], region) : null;

    /// <exception cref="ArgumentException">The profile has no region.</exception>
    public static AwsLocation GetLocation(PublishProfile profile) =>
        TryGetLocation(profile) ?? throw new ArgumentException($"The profile '{profile.Name}' has no {ProfileProperties.PubwizAwsRegion}.", nameof(profile));

    /// <summary>
    /// Why an AWS profile can't publish from here (no aws CLI, settings missing), or <c>null</c> when it can.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="aws">The AWS CLI.</param>
    /// <param name="requiredProperties">The settings this kind of profile needs, e.g. the function name.</param>
    public static string? GetSetupError(PublishProfile profile, AwsCli aws, params string[] requiredProperties)
    {
        if (!aws.IsInstalled)
        {
            return $"The AWS CLI (aws) isn't installed. Install it: {AwsCli.InstallDocsUrl}";
        }

        var missing = requiredProperties.Prepend(ProfileProperties.PubwizAwsRegion).Where(name => profile[name] is null).ToList();
        return missing.Count == 0 ? null : $"The profile '{profile.Name}' has no {string.Join(" or ", missing)}.";
    }

    private static bool IsCustom(PublishProfile profile, string provider) =>
        profile.Method == PublishMethod.Custom && string.Equals(profile[ProfileProperties.PublishProvider], provider, StringComparison.OrdinalIgnoreCase);
}
