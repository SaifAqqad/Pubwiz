using Pubwiz.Core.Infrastructure.Secrets;
using Pubwiz.Core.Targets.WebDeploy;
using Pubwiz.Tool.Rendering;
using System.ComponentModel;

namespace Pubwiz.Tool.Commands;

/// <summary>The server argument shared by the <c>servers</c> commands.</summary>
public class ServerSettings : CommandSettings
{
    [CommandArgument(0, "<server>")]
    [Description("The Web Deploy server as written in profiles: an IP address or name (a port or URL is fine too)")]
    public string Server { get; init; } = string.Empty;

    public override ValidationResult Validate()
    {
        try
        {
            ServerCredentialStore.ToServerKey(Server);
            return ValidationResult.Success();
        }
        catch (ArgumentException ex)
        {
            return ValidationResult.Error(ex.Message);
        }
    }
}

/// <summary>
/// <c>dotnet pubwiz servers set</c>: saves a user name and password for a Web Deploy server. Profiles that deploy to
/// it then sign in with these instead of their own.
/// </summary>
public sealed class ServersSetCommand(IAnsiConsole console, ServerCredentialStore servers) : AsyncCommand<ServersSetCommand.Settings>
{
    public sealed class Settings : ServerSettings
    {
        [CommandOption("-u|--user <NAME>")]
        [Description("The user to sign in as (asked for when missing)")]
        public string? UserName { get; init; }

        [CommandOption("--password-stdin")]
        [Description("Read the password from standard input instead of asking, e.g. in setup scripts")]
        public bool PasswordStdin { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var interactive = console.Profile.Capabilities.Interactive;
        if (!interactive && (settings.UserName is null || !settings.PasswordStdin))
        {
            throw new UserErrorException("There's no terminal to ask in. Pass --user <name> and --password-stdin.");
        }

        var server = ServerCredentialStore.ToServerKey(settings.Server);
        var userName = settings.UserName ?? console.Ask("User name", hint: server, validate: v => string.IsNullOrWhiteSpace(v) ? "Enter a user name" : null);
        var password = settings.PasswordStdin
            ? (await Console.In.ReadLineAsync(cancellationToken))?.TrimEnd('\r', '\n') ?? string.Empty
            : console.AskSecret($"Password for {userName}", hint: server, validate: v => v.Length == 0 ? "Enter the password" : null);

        if (password.Length == 0)
        {
            throw new UserErrorException("The password is empty.");
        }

        ServerCredential saved;
        try
        {
            saved = await servers.SaveAsync(server, userName.Trim(), password, cancellationToken);
        }
        catch (SecretStoreException ex)
        {
            throw new UserErrorException($"The password couldn't be saved: {ex.Message}");
        }

        console.MarkupLine($"{Widgets.SuccessCheckMarkup} Saved the login for [{Widgets.PrimaryColorMarkup}]{saved.Server.EscapeMarkup()}[/] " +
                           $"[dim](password in {servers.SecretStoreName.EscapeMarkup()})[/]");
        console.MarkupLine($"[dim]Web Deploy profiles for {saved.Server.EscapeMarkup()} now sign in as {saved.UserName.EscapeMarkup()} instead of their own user.[/]");
        return ExitCodes.Success;
    }
}

/// <summary><c>dotnet pubwiz servers list</c>: shows the servers with a saved login.</summary>
public sealed class ServersListCommand(IAnsiConsole console, ServerCredentialStore servers) : AsyncCommand<ServersListCommand.Settings>
{
    public sealed class Settings : CommandSettings;

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var saved = servers.List();
        if (saved.Count == 0)
        {
            console.MarkupLine($"[dim]No server logins saved. Add one with: {ToolInfo.CommandName} servers set <server>[/]");
            return ExitCodes.Success;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(new Style(Widgets.PrimaryColor, decoration: Decoration.Dim))
            .AddColumns("Server", "User", "Password");

        foreach (var server in saved)
        {
            table.AddRow(server.Server.EscapeMarkup(), server.UserName.EscapeMarkup(), await DescribePasswordAsync(server, cancellationToken));
        }

        console.MarkupLine($"[dim]{DisplayText.Link(servers.FilePath)}[/]");
        console.Write(table);
        return ExitCodes.Success;
    }

    private async Task<string> DescribePasswordAsync(ServerCredential server, CancellationToken cancellationToken)
    {
        try
        {
            return await servers.GetPasswordAsync(server, cancellationToken) is null ? "[red]missing[/]" : "saved";
        }
        catch (SecretStoreException)
        {
            return "[yellow]can't read[/]";
        }
    }
}

/// <summary><c>dotnet pubwiz servers remove</c>: forgets a server's login; its profiles use their own again.</summary>
public sealed class ServersRemoveCommand(IAnsiConsole console, ServerCredentialStore servers) : AsyncCommand<ServerSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ServerSettings settings, CancellationToken cancellationToken)
    {
        var server = ServerCredentialStore.ToServerKey(settings.Server);
        if (!await servers.RemoveAsync(server, cancellationToken))
        {
            throw new UserErrorException($"There's no login saved for {server}.");
        }

        console.MarkupLine($"{Widgets.SuccessCheckMarkup} Removed the login for [{Widgets.PrimaryColorMarkup}]{server.EscapeMarkup()}[/]");
        return ExitCodes.Success;
    }
}
