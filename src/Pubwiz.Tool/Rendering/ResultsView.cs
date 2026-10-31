using Pubwiz.Core.Execution;
using Pubwiz.Core.Reporting;

namespace Pubwiz.Tool.Rendering;

/// <summary>
/// The table shown after publishing, and the follow-up menu (export logs and reports).
/// </summary>
public sealed class ResultsView(IAnsiConsole console)
{
    /// <summary>The options in the menu shown after publishing.</summary>
    private enum MenuChoice
    {
        Exit,
        ExportLogs,
        ExportJson,
        ExportCsv
    }

    public void WriteResults(IReadOnlyList<PublishJob> jobs)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(new Style(Widgets.PrimaryColor, decoration: Decoration.Dim))
            .AddColumns("Project", "Profile", "Status", "Duration", "Result");

        foreach (var job in jobs)
        {
            var result = job.Status == JobStatus.Succeeded && job.Result is { } location ? DisplayText.ShortPath(location) : job.Error;
            table.AddRow(
                job.Request.Project.Name.EscapeMarkup(),
                job.Request.Profile.Name.EscapeMarkup(),
                StatusIcons.JobStatusText(job.Status),
                StatusIcons.FormatDuration(job.Duration),
                (result ?? string.Empty).EscapeMarkup());
        }

        console.Write(table);
    }

    /// <summary>Offers to export logs and reports until the user picks Exit.</summary>
    public void ShowMenu(IReadOnlyList<PublishJob> jobs)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        while (true)
        {
            var choice = console.Prompt(new SelectionPrompt<MenuChoice>()
                .Title("What next?")
                .HighlightStyle(new Style(Widgets.PrimaryColor))
                .AddChoices(Enum.GetValues<MenuChoice>())
                .UseConverter(Describe));

            switch (choice)
            {
                case MenuChoice.ExportLogs:
                    var folder = AskForPath("Folder for the log files:", $"pubwiz-logs-{timestamp}");
                    var files = PublishExporter.WriteLogs(jobs, folder);
                    console.MarkupLine($"Wrote {files.Count} log file(s) to [{Widgets.PrimaryColorMarkup}]{folder.EscapeMarkup()}[/]");
                    break;

                case MenuChoice.ExportJson:
                    var jsonPath = AskForPath("Report file:", $"pubwiz-report-{timestamp}.json");
                    PublishExporter.WriteJsonReport(jobs, jsonPath);
                    console.MarkupLine($"Wrote [{Widgets.PrimaryColorMarkup}]{jsonPath.EscapeMarkup()}[/]");
                    break;

                case MenuChoice.ExportCsv:
                    var csvPath = AskForPath("Report file:", $"pubwiz-report-{timestamp}.csv");
                    PublishExporter.WriteCsvReport(jobs, csvPath);
                    console.MarkupLine($"Wrote [{Widgets.PrimaryColorMarkup}]{csvPath.EscapeMarkup()}[/]");
                    break;

                default:
                    return;
            }
        }
    }

    private string AskForPath(string question, string defaultValue)
    {
        var answer = console.Prompt(new TextPrompt<string>(question).DefaultValue(defaultValue));
        return Path.GetFullPath(answer);
    }

    private static string Describe(MenuChoice choice) => choice switch
    {
        MenuChoice.ExportLogs => "Export the full logs",
        MenuChoice.ExportJson => "Export a report (JSON)",
        MenuChoice.ExportCsv => "Export a report (CSV)",
        _ => "Exit"
    };
}
