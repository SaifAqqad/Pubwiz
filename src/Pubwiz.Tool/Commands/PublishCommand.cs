using Pubwiz.Tool.Wizard;
using System.ComponentModel;

namespace Pubwiz.Tool.Commands;

/// <summary>
/// The default command: finds projects and walks the user through publishing them.
/// </summary>
public sealed class PublishCommand(IAnsiConsole console, PublishWizard wizard) : AsyncCommand<PublishCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[path]")]
        [Description("Folder, solution (.sln/.slnx) or project (.csproj) to publish. Defaults to the current folder")]
        public string? Path { get; init; }

        [CommandOption("--all")]
        [Description("Also list projects that normally aren't published (libraries, tests, Aspire AppHosts)")]
        public bool IncludeAll { get; init; }

        [CommandOption("--verbose")]
        [Description("Show detailed output")]
        public bool Verbose { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Widgets.WriteHeader();

        var options = new WizardOptions(settings.Path ?? Directory.GetCurrentDirectory(), settings.IncludeAll);
        var selections = await wizard.RunAsync(options, cancellationToken);

        WriteSummary(selections);
        return ExitCodes.Success;
    }

    private void WriteSummary(IReadOnlyList<PublishSelection> selections)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(new Style(Widgets.PrimaryColor, decoration: Decoration.Dim))
            .AddColumns("Project", "Profile", "Method");

        foreach (var (project, profile) in selections)
        {
            table.AddRow(project.Name.EscapeMarkup(), profile.Name.EscapeMarkup(), profile.Method.ToString());
        }

        console.WriteLine();
        console.Write(table);
        console.MarkupLine("[yellow]Publishing itself isn't implemented yet; this run only prepared the profiles.[/]");
    }
}
