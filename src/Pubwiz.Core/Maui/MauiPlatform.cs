namespace Pubwiz.Core.Maui;

/// <summary>The mobile platforms Pubwiz publishes MAUI apps for.</summary>
public enum MauiPlatform
{
    Android,
    Ios
}

public static class MauiPlatforms
{
    /// <summary>The target framework suffix, e.g. <c>net10.0-android</c> ends with <c>-android</c>.</summary>
    public static string TargetFrameworkSuffix(this MauiPlatform platform) => platform == MauiPlatform.Android ? "-android" : "-ios";

    /// <summary>The workloads that can build the platform (either one is enough).</summary>
    public static IReadOnlyList<string> Workloads(this MauiPlatform platform) =>
        platform == MauiPlatform.Android ? ["maui-android", "android", "maui"] : ["maui-ios", "ios", "maui"];

    /// <summary>The workload to suggest installing.</summary>
    public static string RecommendedWorkload(this MauiPlatform platform) => platform == MauiPlatform.Android ? "maui-android" : "maui-ios";

    /// <summary>The project's target frameworks for this platform, e.g. <c>net10.0-android</c>.</summary>
    public static IReadOnlyList<string> TargetFrameworks(this MauiPlatform platform, IEnumerable<string> projectFrameworks) =>
        projectFrameworks.Where(tfm => tfm.Contains(platform.TargetFrameworkSuffix(), StringComparison.OrdinalIgnoreCase)).ToList();
}
