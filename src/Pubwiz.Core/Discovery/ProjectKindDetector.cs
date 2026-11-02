using Pubwiz.Core.Dotnet;

namespace Pubwiz.Core.Discovery;

/// <summary>
/// Works out a <see cref="ProjectKind"/> from evaluated MSBuild properties.
/// </summary>
public static class ProjectKindDetector
{
    /// <summary>
    /// The MSBuild properties <see cref="Detect"/> looks at.
    /// </summary>
    public static IReadOnlyList<string> RequiredProperties { get; } =
    [
        "OutputType",
        "IsTestProject",
        "IsAspireHost",
        "UseMaui",
        "UseWPF",
        "UseWindowsForms",
        "AzureFunctionsVersion",
        "AWSProjectType",
        "UsingMicrosoftNETSdkWeb",
        "UsingMicrosoftNETSdkWorker",
        "UsingMicrosoftNETSdkBlazorWebAssembly"
    ];

    /// <summary>
    /// The MSBuild items <see cref="Detect"/> looks at.
    /// </summary>
    public static IReadOnlyList<string> RequiredItems { get; } = ["PackageReference"];

    /// <summary>
    /// Packages that mark a test project. <c>IsTestProject</c> alone isn't enough because
    /// it comes from package props, which only exist after a restore.
    /// </summary>
    private static readonly HashSet<string> TestPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.NET.Test.Sdk", "xunit", "xunit.v3", "NUnit", "MSTest", "MSTest.TestFramework", "TUnit"
    };

    /// <summary>
    /// Packages of Lambda function projects. The class library templates have no <c>OutputType</c>, so without these
    /// they would look like libraries.
    /// </summary>
    private static readonly HashSet<string> LambdaPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Amazon.Lambda.Core", "Amazon.Lambda.RuntimeSupport", "Amazon.Lambda.Annotations"
    };

    /// <summary>
    /// Order matters: the more specific SDKs and flags are checked first
    /// (a MAUI or Functions app is also an executable, a Blazor app also uses the Razor SDK, ...).
    /// </summary>
    public static ProjectKind Detect(MsBuildEvaluation project)
    {
        if (project.IsTrue("IsTestProject") || project.GetItems("PackageReference").Any(p => TestPackages.Contains(p.Identity)))
        {
            return ProjectKind.Test;
        }

        if (project.IsTrue("IsAspireHost"))
        {
            return ProjectKind.AspireHost;
        }

        if (project.IsTrue("UseMaui"))
        {
            return ProjectKind.Maui;
        }

        if (project.GetProperty("AzureFunctionsVersion") is not null)
        {
            return ProjectKind.AzureFunctions;
        }

        // ASP.NET Core apps that can also run on Lambda stay web apps (the Lambda target is offered for those too).
        var isLambda = string.Equals(project.GetProperty("AWSProjectType"), "Lambda", StringComparison.OrdinalIgnoreCase)
                       || project.GetItems("PackageReference").Any(p => LambdaPackages.Contains(p.Identity));
        if (isLambda && !project.IsTrue("UsingMicrosoftNETSdkWeb"))
        {
            return ProjectKind.AwsLambda;
        }

        if (project.IsTrue("UsingMicrosoftNETSdkBlazorWebAssembly"))
        {
            return ProjectKind.BlazorWasm;
        }

        if (project.IsTrue("UsingMicrosoftNETSdkWeb"))
        {
            return ProjectKind.Web;
        }

        if (project.IsTrue("UsingMicrosoftNETSdkWorker"))
        {
            return ProjectKind.Worker;
        }

        if (project.IsTrue("UseWPF") || project.IsTrue("UseWindowsForms"))
        {
            return ProjectKind.Desktop;
        }

        return project.GetProperty("OutputType")?.ToLowerInvariant() switch
        {
            "exe" or "winexe" => ProjectKind.Console,
            "library" => ProjectKind.Library,
            _ => ProjectKind.Unknown
        };
    }
}
