using Pubwiz.Core.Publishing;
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
            // Output folders, files and site URLs are links; errors are plain text.
            var result = job.Status == JobStatus.Succeeded && job.Result is { } location ? DisplayText.Link(location) : (job.Error ?? string.Empty).EscapeMarkup();
            table.AddRow(
                job.Request.Project.Name.EscapeMarkup(),
                job.Request.Profile.Name.EscapeMarkup(),
                StatusIcons.JobStatusText(job.Status),
                StatusIcons.FormatDuration(job.Duration),
                result);
        }

        console.Write(table);
    }

    /// <summary>Offers to export logs and reports until the user picks Exit.</summary>
    public void ShowMenu(IReadOnlyList<PublishJob> jobs)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        while (true)
        {
            var choice = console.Prompt(Questions.Menu<MenuChoice>("What next?")
                .AddChoices(Enum.GetValues<MenuChoice>())
                .AddCancelResult(MenuChoice.Exit)
                .UseConverter(Describe));

            switch (choice)
            {
                case MenuChoice.ExportLogs:
                    var folder = AskForPath("Folder for the log files", $"pubwiz-logs-{timestamp}");
                    var files = PublishExporter.WriteLogs(jobs, folder);
                    console.MarkupLine($"Wrote {files.Count} log file(s) to [{Widgets.PrimaryColorMarkup}]{DisplayText.Link(folder)}[/]");
                    break;

                case MenuChoice.ExportJson:
                    var jsonPath = AskForPath("Report file", $"pubwiz-report-{timestamp}.json");
                    PublishExporter.WriteJsonReport(jobs, jsonPath);
                    console.MarkupLine($"Wrote [{Widgets.PrimaryColorMarkup}]{DisplayText.Link(jsonPath)}[/]");
                    break;

                case MenuChoice.ExportCsv:
                    var csvPath = AskForPath("Report file", $"pubwiz-report-{timestamp}.csv");
                    PublishExporter.WriteCsvReport(jobs, csvPath);
                    console.MarkupLine($"Wrote [{Widgets.PrimaryColorMarkup}]{DisplayText.Link(csvPath)}[/]");
                    break;

                default:
                    return;
            }
        }
    }

    private string AskForPath(string question, string defaultValue)
    {
        var answer = console.Ask(question, defaultValue: defaultValue);
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
