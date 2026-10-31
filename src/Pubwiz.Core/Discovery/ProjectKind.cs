namespace Pubwiz.Core.Discovery;

/// <summary>
/// The kind of project, which decides the publish targets offered for it.
/// </summary>
public enum ProjectKind
{
    Unknown,

    /// <summary>ASP.NET Core app (<c>Microsoft.NET.Sdk.Web</c>).</summary>
    Web,

    /// <summary>Standalone Blazor WebAssembly app (<c>Microsoft.NET.Sdk.BlazorWebAssembly</c>).</summary>
    BlazorWasm,

    /// <summary>Worker service (<c>Microsoft.NET.Sdk.Worker</c>).</summary>
    Worker,

    /// <summary>Azure Functions app (<c>AzureFunctionsVersion</c> is set).</summary>
    AzureFunctions,

    /// <summary>.NET MAUI app (<c>UseMaui</c>).</summary>
    Maui,

    /// <summary>WPF or Windows Forms app.</summary>
    Desktop,

    /// <summary>Any other executable.</summary>
    Console,

    /// <summary>Class library: not publishable on its own.</summary>
    Library,

    /// <summary>Test project: not publishable.</summary>
    Test,

    /// <summary>.NET Aspire AppHost: deployed with the Aspire CLI, not published.</summary>
    AspireHost
}

public static class ProjectKindExtensions
{
    /// <summary>
    /// <c>false</c> for project kinds that are hidden from the wizard by default.
    /// </summary>
    public static bool IsPublishable(this ProjectKind kind) => kind is not (ProjectKind.Library or ProjectKind.Test or ProjectKind.AspireHost or ProjectKind.Unknown);

    public static string DisplayName(this ProjectKind kind) => kind switch
    {
        ProjectKind.Web => "Web",
        ProjectKind.BlazorWasm => "Blazor WebAssembly",
        ProjectKind.Worker => "Worker",
        ProjectKind.AzureFunctions => "Azure Functions",
        ProjectKind.Maui => "MAUI",
        ProjectKind.Desktop => "Desktop",
        ProjectKind.Console => "Console",
        ProjectKind.Library => "Library",
        ProjectKind.Test => "Test",
        ProjectKind.AspireHost => "Aspire AppHost",
        _ => "Unknown"
    };
}
