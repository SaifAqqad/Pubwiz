using Microsoft.Extensions.DependencyInjection;
using Pubwiz.Core;
using Pubwiz.Core.Infrastructure;
using Pubwiz.Tool;
using Pubwiz.Tool.Commands;
using Pubwiz.Tool.Infrastructure;
using System.Text;

// Box-drawing characters and spinners need UTF-8.
Console.OutputEncoding = Encoding.UTF8;
CtrlC.Install(AnsiConsole.Console);

// Ctrl+C can stop a prompt that's waiting for a key (see InterruptibleConsole).
var console = new InterruptibleConsole(AnsiConsole.Console);

var services = new ServiceCollection().AddPubwiz(console);
var app = new CommandApp<PublishCommand>(new TypeRegistrar(services));

app.Configure(config =>
{
    // Spectre.Console.Cli registers its console for injection itself, so it has to be this one too.
    config.ConfigureConsole(console);
    config.SetApplicationName(ToolInfo.CommandName);
    config.SetApplicationVersion(ToolInfo.Version);
    config.SetExceptionHandler((exception, _) => HandleException(exception));

    config.AddCommand<TransformCommand>("transform")
        .WithDescription("Write a transformed web.config (same transforms as publishing) without publishing");

    config.AddBranch("presets", presets =>
    {
        presets.SetDescription("List and export the batch presets saved in pubwiz.json");
        presets.AddCommand<PresetsListCommand>("list").WithDescription("Show the saved presets");
        presets.AddCommand<PresetsExportCommand>("export").WithDescription("Write one preset to its own file");
    });

    config.AddBranch("servers", servers =>
    {
        servers.SetDescription("Save Web Deploy logins per server; they replace the user and password of profiles for that server");
        servers.AddCommand<ServersSetCommand>("set").WithDescription("Save the user name and password for a server");
        servers.AddCommand<ServersListCommand>("list").WithDescription("Show the servers with a saved login");
        servers.AddCommand<ServersRemoveCommand>("remove").WithDescription("Forget a server's login");
    });
});

var exitCode = await app.RunAsync(args);

AnsiConsole.Cursor.Show();
return exitCode;

static int HandleException(Exception exception)
{
    AnsiConsole.Cursor.Show();

    // Dependency injection wraps exceptions thrown while creating services, so look at the whole chain.
    for (var current = exception; current is not null; current = current.InnerException)
    {
        switch (current)
        {
            case OperationCanceledException:
                Widgets.Write(new Markup("[dim]Cancelled[/]"));
                return ExitCodes.Cancelled;

            case PrerequisiteMissingException missing:
                Widgets.Write(Widgets.Error(missing.Message));
                return ExitCodes.MissingPrerequisite;

            case UserErrorException userError:
                Widgets.Write(Widgets.Error(userError.Message));
                return userError.ExitCode;
        }
    }

    Widgets.Write(Widgets.Error(exception.Message));
#if DEBUG
    AnsiConsole.WriteException(exception, ExceptionFormats.ShortenPaths);
#endif
    return ExitCodes.UnhandledError;
}
