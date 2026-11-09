using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Publishing.Steps;

namespace Pubwiz.Core.Targets.Maui;

/// <summary>
/// Publishes a .NET MAUI app for Android (.aab/.apk) or iOS (.ipa) with <c>dotnet publish -f net*-android|ios</c>.
/// </summary>
/// <remarks>
/// Profiles use <c>WebPublishMethod=Custom</c> with <c>PublishProvider=MauiAndroid|MauiIos</c> and the SDK's own
/// signing properties. The Android keystore password is never written anywhere: it's passed as
/// <c>AndroidSigningKeyPass=env:...</c>, which the Android SDK resolves from an environment variable.
/// Registered once per platform.
/// </remarks>
public sealed class MauiTarget(DotnetCli dotnet, MauiPlatform platform) : IPublishTarget
{
    /// <summary>The environment variable the signing password is passed in.</summary>
    internal const string SigningPasswordVariable = "PUBWIZ_ANDROID_SIGNING_PASSWORD";

    public PublishTargetType Type => platform == MauiPlatform.Android ? PublishTargetType.MauiAndroid : PublishTargetType.MauiIos;

    public bool CanPublish(PublishProfile profile) =>
        profile.Method == PublishMethod.Custom
        && string.Equals(profile[ProfileProperties.PublishProvider], platform == MauiPlatform.Android ? ProfileProperties.MauiAndroidProvider : ProfileProperties.MauiIosProvider, StringComparison.OrdinalIgnoreCase);

    /// <summary>Android signing needs the keystore password; iOS signing uses the keychain.</summary>
    public bool RequiresPassword(PublishProfile profile) =>
        platform == MauiPlatform.Android && string.Equals(profile[ProfileProperties.AndroidKeyStore], "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>The profile's Android or iOS runtime decides it.</summary>
    public TargetPlatform GetPlatform(PublishProfile profile) => TargetPlatform.None;

    public IReadOnlyList<PublishStep> CreateSteps(PublishRequest request)
    {
        var outputDirectory = StagingPublish.GetStagingDirectory(request);
        return
        [
            new CheckWorkloadStep(dotnet, platform),
            new DotnetPublishStep(dotnet, CreateOptions(request, outputDirectory), outputDirectory),
            new CollectPackagesStep(platform)
        ];
    }

    internal DotnetPublishOptions CreateOptions(PublishRequest request, string outputDirectory)
    {
        var profile = request.Profile;
        var properties = new Dictionary<string, string> { [ProfileProperties.PublishDir] = outputDirectory + Path.DirectorySeparatorChar };
        var secrets = new Dictionary<string, string>();

        if (RequiresPassword(profile) && request.Password is not null)
        {
            properties[ProfileProperties.AndroidSigningKeyPass] = $"env:{SigningPasswordVariable}";
            properties[ProfileProperties.AndroidSigningStorePass] = $"env:{SigningPasswordVariable}";
            secrets[SigningPasswordVariable] = request.Password;
        }

        return new DotnetPublishOptions(request.Project.FilePath)
        {
            Configuration = request.EffectiveConfiguration,
            ProfileName = profile.Name,
            TargetFramework = profile[ProfileProperties.TargetFramework] ?? throw new ArgumentException($"The profile '{profile.Name}' has no TargetFramework (e.g. net10.0-android)."),
            RuntimeIdentifier = profile[ProfileProperties.RuntimeIdentifier],
            Properties = properties,
            SecretProperties = secrets,
            Verbose = request.Verbose
        };
    }

    /// <summary>Fails early, with the install command, when the MAUI workload for the platform is missing.</summary>
    private sealed class CheckWorkloadStep(DotnetCli dotnet, MauiPlatform platform) : PublishStep("Check workload")
    {
        public override async Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
        {
            if (platform == MauiPlatform.Ios && !OperatingSystem.IsMacOS())
            {
                throw new PublishStepException("iOS apps can only be built on macOS (Xcode is required).");
            }

            var installed = await dotnet.ListWorkloadsAsync(cancellationToken);
            if (!platform.Workloads().Any(installed.Contains))
            {
                throw new PublishStepException($"The {platform.RecommendedWorkload()} workload isn't installed. Run: dotnet workload install {platform.RecommendedWorkload()}");
            }

            context.Log.Info($"Installed workloads: {string.Join(", ", installed)}");
        }
    }

    /// <summary>Finds the packages the build produced and lists them as the result.</summary>
    internal sealed class CollectPackagesStep(MauiPlatform platform) : PublishStep("Collect packages")
    {
        public override Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
        {
            var packages = FindPackages(context.OutputDirectory!, platform);
            if (packages.Count == 0)
            {
                throw new PublishStepException($"No {(platform == MauiPlatform.Android ? ".aab/.apk" : ".ipa")} was found in {context.OutputDirectory}.");
            }

            foreach (var package in packages)
            {
                context.Log.Info($"Package: {package}");
            }

            context.PackagePath = packages[0];
            context.Destination = string.Join(", ", packages);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Android builds write an unsigned and a <c>-Signed</c> copy; only the signed ones are worth showing when they exist.
        /// </summary>
        internal static IReadOnlyList<string> FindPackages(string folder, MauiPlatform platform)
        {
            string[] extensions = platform == MauiPlatform.Android ? [".aab", ".apk"] : [".ipa"];
            var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)
                .ToList();

            var signed = files.Where(f => Path.GetFileNameWithoutExtension(f).EndsWith("-Signed", StringComparison.OrdinalIgnoreCase)).ToList();
            return signed.Count > 0 ? signed : files;
        }
    }
}
