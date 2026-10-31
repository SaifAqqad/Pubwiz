using Pubwiz.Core.Discovery;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Questions asked by several target pages.
/// </summary>
public static class CommonPrompts
{
    /// <summary>Asks only when the project targets several frameworks.</summary>
    public static string ChooseTargetFramework(IAnsiConsole console, ProjectInfo project)
    {
        return project.TargetFrameworks.Count switch
        {
            0 => throw new UserErrorException($"{project.Name} has no target framework."),
            1 => project.TargetFrameworks[0],
            _ => console.Prompt(new SelectionPrompt<string>()
                .Title("Target framework:")
                .HighlightStyle(new Style(Widgets.PrimaryColor))
                .AddChoices(project.TargetFrameworks))
        };
    }

    public static string ChooseConfiguration(IAnsiConsole console)
    {
        return console.Prompt(new SelectionPrompt<string>()
            .Title("Configuration:")
            .HighlightStyle(new Style(Widgets.PrimaryColor))
            .AddChoices("Release", "Debug"));
    }

    /// <summary>
    /// Asks for an existing file, accepting <c>~</c> for the home folder and paths relative to the current folder.
    /// </summary>
    public static string AskForFile(IAnsiConsole console, string question, params string[] allowedExtensions)
    {
        var answer = console.Prompt(new TextPrompt<string>(question).Validate(value =>
        {
            var path = ExpandPath(value);
            if (!File.Exists(path))
            {
                return ValidationResult.Error("[red]File not found[/]");
            }

            return allowedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                ? ValidationResult.Success()
                : ValidationResult.Error($"[red]Expected a {string.Join(" or ", allowedExtensions)} file[/]");
        }));

        return ExpandPath(answer);
    }

    private static string ExpandPath(string path)
    {
        var trimmed = path.Trim().Trim('"', '\'');
        if (trimmed == "~" || trimmed.StartsWith("~/", StringComparison.Ordinal))
        {
            trimmed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), trimmed.TrimStart('~', '/'));
        }

        return Path.GetFullPath(trimmed);
    }
}
