using Spectre.Console.Rendering;

namespace Pubwiz.Tool;

/// <summary>
/// Reusable renderables, so every screen looks the same.
/// </summary>
public static partial class Widgets
{
    public static Color PrimaryColor => Color.MediumPurple;

    public static string PrimaryColorMarkup { get; } = PrimaryColor.ToMarkup();

    public static IRenderable Version { get; } = new Markup($"[{PrimaryColorMarkup} dim]v{ToolInfo.Version}[/]");

    /// <summary>
    /// One-line header with the version, for small terminals.
    /// </summary>
    public static IRenderable SmallHeader { get; } = new Markup($"[{PrimaryColorMarkup} bold][link={ToolInfo.ProjectUrl}]Pubwiz[/][/] [{PrimaryColorMarkup} dim]v{ToolInfo.Version}[/]");

    /// <summary>
    /// Figlet header with the version underneath, for roomy terminals.
    /// </summary>
    public static IRenderable LargeHeader { get; } = new Rows(new FigletText("Pubwiz").LeftJustified().Color(PrimaryColor), Version);

    public static IRenderable Header() => IsSmallTerminal() ? SmallHeader : LargeHeader;

    public static IRenderable Error(string message) => new Markup($"[red bold]Error[/] {message.EscapeMarkup()}");

    public static IRenderable Warning(string message) => new Markup($"[yellow bold]Warning[/] {message.EscapeMarkup()}");

    public static IRenderable SuccessCheck { get; } = new Markup("[green]✓[/]");

    public static IRenderable FailureCross { get; } = new Markup("[red]✕[/]");

    /// <summary>
    /// <c>true</c> for terminals too small for the figlet header.
    /// </summary>
    public static bool IsSmallTerminal() => AnsiConsole.Profile.Height <= 20 || AnsiConsole.Profile.Width <= 90;
}
