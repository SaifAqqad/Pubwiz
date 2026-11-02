using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Targets.WebDeploy;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Asks, before publishing with Web Deploy (IIS), whether to take the app offline (<c>app_offline.htm</c>) while copying.
/// A running ASP.NET Core app keeps its DLLs locked, which otherwise fails with ERROR_FILE_IN_USE.
/// </summary>
/// <remarks>
/// Answers are remembered for the run, like passwords: the wizard asks right after "Publish now?" (so Esc goes back to
/// the projects), and publishing then reuses the answer. Esc throws <see cref="PromptCancelledException"/>.
/// </remarks>
public sealed class AppOfflinePrompt(IAnsiConsole console, PublishRunner runner)
{
    private readonly Dictionary<PublishProfile, bool> _answers = new(ReferenceEqualityComparer.Instance);

    /// <returns>The answer for a Web Deploy profile published with msdeploy.exe, or <c>null</c> for any other profile.</returns>
    /// <remarks>Only msdeploy.exe takes the app offline; the built-in cross-platform client doesn't, so it isn't asked.</remarks>
    public bool? Ask(ProjectInfo project, PublishProfile profile, bool showProjectName)
    {
        if (runner.FindTarget(profile) is not WebDeployTarget target || target.GetSetupError(profile) is not null || !target.UsesMsDeploy)
        {
            return null;
        }

        if (_answers.TryGetValue(profile, out var answer))
        {
            return answer;
        }

        var question = showProjectName ? $"Take {project.Name} offline while publishing?" : "Take the app offline while publishing?";
        answer = console.AskYesNo(question, defaultValue: true, hint: "app_offline.htm, so locked files can be replaced");
        _answers[profile] = answer;
        return answer;
    }
}
