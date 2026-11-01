using Pubwiz.Core.Publishing;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pubwiz.Core.Reporting;

/// <summary>
/// Writes publish logs and reports to disk, on request.
/// </summary>
public static class PublishExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Writes one <c>&lt;project&gt;.log</c> file per job into <paramref name="folder"/>.
    /// </summary>
    /// <returns>The files written.</returns>
    public static IReadOnlyList<string> WriteLogs(IReadOnlyList<PublishJob> jobs, string folder)
    {
        Directory.CreateDirectory(folder);
        var files = new List<string>();

        // Two jobs for the same project (different profiles) get the profile in the file name.
        var duplicateNames = jobs.GroupBy(j => j.Request.Project.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

        foreach (var job in jobs)
        {
            var name = duplicateNames.Contains(job.Request.Project.Name)
                ? $"{job.Request.Project.Name}.{job.Request.Profile.Name}"
                : job.Request.Project.Name;

            var path = Path.Combine(folder, $"{name}.log");
            File.WriteAllLines(path, job.Log.Snapshot().Select(FormatLogLine));
            files.Add(path);
        }

        return files;
    }

    public static void WriteJsonReport(IReadOnlyList<PublishJob> jobs, string filePath)
    {
        CreateParentFolder(filePath);
        var entries = jobs.Select(PublishReportEntry.FromJob).ToList();
        File.WriteAllText(filePath, JsonSerializer.Serialize(entries, JsonOptions));
    }

    public static void WriteCsvReport(IReadOnlyList<PublishJob> jobs, string filePath)
    {
        CreateParentFolder(filePath);
        var csv = new StringBuilder();
        csv.AppendLine("Project,ProjectPath,Profile,Target,Configuration,Status,StartedAt,FinishedAt,DurationSeconds,FailedStep,Error,OutputDirectory,Destination,LogLines,GitBranch,GitCommit,GitCommitDate,GitHasChanges");

        foreach (var entry in jobs.Select(PublishReportEntry.FromJob))
        {
            string?[] values =
            [
                entry.Project, entry.ProjectPath, entry.Profile, entry.Target, entry.Configuration, entry.Status,
                entry.StartedAt?.ToString("o"), entry.FinishedAt?.ToString("o"),
                entry.DurationSeconds?.ToString(CultureInfo.InvariantCulture),
                entry.FailedStep, entry.Error, entry.OutputDirectory, entry.Destination,
                entry.LogLines.ToString(CultureInfo.InvariantCulture),
                entry.GitBranch, entry.GitCommit, entry.GitCommitDate?.ToString("o"),
                entry.GitHasChanges is { } hasChanges ? (hasChanges ? "true" : "false") : null
            ];

            csv.AppendLine(string.Join(',', values.Select(EscapeCsv)));
        }

        File.WriteAllText(filePath, csv.ToString());
    }

    private static void CreateParentFolder(string filePath) => Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);

    /// <summary>e.g. <c>12:03:44.120 [Build and publish] out: Restored /src/Api/Api.csproj</c></summary>
    internal static string FormatLogLine(LogEntry entry)
    {
        var source = entry.Source switch
        {
            LogSource.Output => "out",
            LogSource.Error => "err",
            _ => "pubwiz"
        };

        var step = entry.Step is null ? string.Empty : $"[{entry.Step}] ";
        return $"{entry.Timestamp:HH:mm:ss.fff} {step}{source}: {entry.Text}";
    }

    /// <summary>RFC 4180: quote values containing commas, quotes or line breaks, and double the quotes.</summary>
    internal static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
