using Pubwiz.Core.Targets;
using Pubwiz.Core.Targets.WebDeploy;
using Pubwiz.WebDeploy;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Explains why Web Deploy can't publish from this machine and suggests other targets for the same server,
/// while still allowing Web Deploy (e.g. for a profile used on a Windows machine or in CI).
/// </summary>
public sealed class WebDeployAlternativesPrompt(IAnsiConsole console)
{
    /// <summary>The user's answer.</summary>
    public abstract record Choice
    {
        /// <summary>Use another target instead.</summary>
        public sealed record Alternative(PublishTargetType Type) : Choice;

        /// <summary>Go ahead with Web Deploy.</summary>
        public sealed record KeepWebDeploy : Choice;

        public sealed record Back : Choice;
    }

    /// <param name="endpoint">The server, when known; it decides which targets are suggested.</param>
    /// <param name="reason">Why Web Deploy can't publish from this machine, shown above the choices.</param>
    /// <param name="keepLabel">The "use Web Deploy anyway" option, as markup.</param>
    public Choice Ask(WebDeployEndpoint? endpoint, string reason, string keepLabel)
    {
        console.MarkupLine($"[yellow]{reason.EscapeMarkup()}[/]");

        var alternatives = WebDeployAlternatives.SuggestFor(endpoint).Select(type => new Choice.Alternative(type));
        var server = endpoint is null ? "the server" : endpoint.ServiceUrl.Host;

        return console.Prompt(Questions.Menu<Choice>($"How do you want to publish to {server.EscapeMarkup()}?")
            .AddChoices(alternatives)
            .AddChoices(new Choice.KeepWebDeploy(), new Choice.Back())
            .AddCancelResult(new Choice.Back())
            .UseConverter(choice => Describe(choice, keepLabel)));
    }

    private static string Describe(Choice choice, string keepLabel) => choice switch
    {
        Choice.Alternative { Type: var type } => type switch
        {
            PublishTargetType.AzureAppService => "Azure App Service [dim]· if the server is an Azure web app (zip deploy, works everywhere)[/]",
            PublishTargetType.Ftp => "FTP / FTPS [dim]· IIS's built-in FTP server[/]",
            PublishTargetType.Sftp => "SFTP [dim]· if the server runs OpenSSH[/]",
            PublishTargetType.ZipFile => "Zip file [dim]· publish to a .zip and copy it to the server yourself[/]",
            _ => type.ToString()
        },
        Choice.KeepWebDeploy => keepLabel,
        _ => "[dim]← Back[/]"
    };
}
