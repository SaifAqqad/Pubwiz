using Microsoft.Extensions.DependencyInjection;
using Pubwiz.Core;
using Pubwiz.Tool;
using Pubwiz.Tool.Commands;
using Pubwiz.Tool.Infrastructure;
using System.Text;

// Box-drawing characters and spinners need UTF-8.
Console.OutputEncoding = Encoding.UTF8;

var services = new ServiceCollection().AddPubwiz();
var app = new CommandApp<PublishCommand>(new TypeRegistrar(services));

app.Configure(config =>
{
    config.SetApplicationName(ToolInfo.CommandName);
    config.SetApplicationVersion(ToolInfo.Version);
    config.SetExceptionHandler((exception, _) => HandleException(exception));

    config.AddCommand<TransformCommand>("transform")
        .WithDescription("Write a transformed web.config (same transforms as publishing) without publishing");
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
