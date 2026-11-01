using Pubwiz.Core.Discovery;
using Pubwiz.Tool.Infrastructure;

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
            _ => console.Choose("Target framework", project.TargetFrameworks)
        };
    }

    public static string ChooseConfiguration(IAnsiConsole console) => console.Choose("Configuration", ["Release", "Debug"]);

    /// <summary>
    /// Asks for an existing file, accepting <c>~</c> for the home folder and paths relative to the current folder.
    /// </summary>
    public static string AskForFile(IAnsiConsole console, string question, params string[] allowedExtensions)
    {
        var answer = console.Ask(question, hint: string.Join(" or ", allowedExtensions), validate: value =>
        {
            var path = UserPath.ToFullPath(value);
            if (!File.Exists(path))
            {
                return "File not found";
            }

            return allowedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                ? null
                : $"Expected a {string.Join(" or ", allowedExtensions)} file";
        });

        return UserPath.ToFullPath(answer);
    }
}
