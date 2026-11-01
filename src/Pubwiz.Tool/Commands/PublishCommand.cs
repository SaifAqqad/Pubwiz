using Pubwiz.Core.Infrastructure.Git;
using Pubwiz.Core.Infrastructure.Platform;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Reporting;
using Pubwiz.Tool.Credentials;
using Pubwiz.Tool.Infrastructure;
using Pubwiz.Tool.Rendering;
using Pubwiz.Tool.Wizard;
using System.ComponentModel;

namespace Pubwiz.Tool.Commands;

/// <summary>
/// The default command: chooses what to publish (preset, arguments or the wizard), then publishes it.
/// </summary>
public sealed class PublishCommand(
    IAnsiConsole console,
    PublishWizard wizard,
    PresetPage presets,
    ConfirmPage confirmPage,
    ArgumentSelection argumentSelection,
    PasswordProvider passwords,
    AppOfflinePrompt appOffline,
    PublishRunner runner,
    GitCli git,
    PublishDashboard dashboard,
    ResultsView results) : AsyncCommand<PublishCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[path]")]
        [Description("Folder, solution (.sln/.slnx) or project (.csproj) to publish. Defaults to the current folder")]
        public string? Path { get; init; }

        [CommandOption("-c|--configuration")]
        [Description("Build configuration, overriding the profiles' (e.g. Release or Debug)")]
        public string? Configuration { get; init; }

        [CommandOption("--preset <NAME>")]
        [Description("Run a saved preset from pubwiz.json instead of the wizard")]
        public string? Preset { get; init; }

        [CommandOption("--preset-file <FILE>")]
        [Description("The preset file to use (default: pubwiz.json in the folder or its parents)")]
        public string? PresetFile { get; init; }

        [CommandOption("--project <NAME>")]
        [Description("Publish this project (name or .csproj path) without the wizard; repeatable. Needs --profile")]
        public string[] Projects { get; init; } = [];

        [CommandOption("--profile <NAME>")]
        [Description("The publish profile used with --project")]
        public string? Profile { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Don't ask for confirmation, and skip the menu after publishing")]
        public bool Yes { get; init; }

        [CommandOption("--all")]
        [Description("Also list projects that normally aren't published (libraries, tests, Aspire AppHosts)")]
        public bool IncludeAll { get; init; }

        [CommandOption("--show-log")]
        [Description("Show the live build output while publishing (toggle with L)")]
        public bool ShowLog { get; init; }

        [DefaultValue(10)]
        [CommandOption("--log-lines")]
        [Description("How many lines of live output to show per project")]
        public int LogLines { get; init; }

        [CommandOption("--parallel")]
        [Description("How many projects to publish at the same time (default 1, or the preset's)")]
        public int? Parallel { get; init; }

        [CommandOption("--report <FILE>")]
        [Description("Write a report after publishing (.json or .csv)")]
        public string? Report { get; init; }

        [CommandOption("--log-dir <FOLDER>")]
        [Description("Write each project's full log to this folder after publishing")]
        public string? LogDirectory { get; init; }

        [CommandOption("--verbose")]
        [Description("Use detailed build output")]
        public bool Verbose { get; init; }

        public override ValidationResult Validate()
        {
            if (LogLines < 1)
            {
                return ValidationResult.Error("--log-lines must be at least 1");
            }

            if (Parallel is < 1)
            {
                return ValidationResult.Error("--parallel must be at least 1");
            }

            return Projects.Length > 0 && (Preset is not null || PresetFile is not null)
                ? ValidationResult.Error("Use either --project or --preset, not both")
                : ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Widgets.WriteHeader();

        // No terminal (output piped, CI): never prompt, fail with a clear message instead.
        var interactive = console.Profile.Capabilities.Interactive && !EnvironmentVariables.IsCi;

        if (await ChooseBatchAsync(settings, interactive, cancellationToken) is not { } batch)
        {
            return ExitCodes.Cancelled;
        }

        var requests = await CreateRequestsAsync(batch, settings, interactive, cancellationToken);
        var jobs = requests.Select(runner.CreateJob).ToList();

        console.WriteLine();
        await RunWithCtrlCAsync(jobs, settings.Parallel ?? batch.Parallel ?? 1, settings);

        results.WriteResults(jobs);
        WriteRequestedExports(jobs, settings);
        if (interactive && !settings.Yes)
        {
            results.ShowMenu(jobs);
        }

        return GetExitCode(jobs);
    }

    private static string GetStartDirectory(Settings settings)
    {
        var path = System.IO.Path.GetFullPath(settings.Path ?? Directory.GetCurrentDirectory());
        return Directory.Exists(path) ? path : System.IO.Path.GetDirectoryName(path)!;
    }

    /// <summary>
    /// Chooses what to publish (a preset, the command line or the wizard) and asks before publishing.
    /// </summary>
    /// <returns>The batch, or <c>null</c> when the user exits or cancels.</returns>
    private async Task<PublishBatch?> ChooseBatchAsync(Settings settings, bool interactive, CancellationToken cancellationToken)
    {
        var path = settings.Path ?? Directory.GetCurrentDirectory();
        var startDirectory = GetStartDirectory(settings);

        // Started now so it runs while the projects load. The fetch (for commits to pull) is skipped in CI.
        var gitInfo = git.GetInfoAsync(startDirectory, fetch: interactive, cancellationToken);

        if (settings.Preset is not null || settings.PresetFile is not null)
        {
            return await ConfirmAsync(await presets.LoadAsync(settings.Preset, settings.PresetFile, startDirectory, cancellationToken));
        }

        if (settings.Projects.Length > 0)
        {
            return await ConfirmAsync(await argumentSelection.SelectAsync(path, settings.Projects, settings.Profile, cancellationToken));
        }

        if (!interactive)
        {
            throw new UserErrorException("There's no terminal for the wizard. Use --preset <name>, or --project <name> with --profile <name>.");
        }

        if (await presets.OfferAsync(startDirectory, cancellationToken) is { } preset)
        {
            return await ConfirmAsync(preset);
        }

        // The wizard asks before publishing itself, since cancelling there goes back to its project list.
        // It shows the git line above its project list.
        return await wizard.RunAsync(new WizardOptions(path, settings.IncludeAll, Confirm: !settings.Yes, Git: gitInfo), cancellationToken);

        async Task<PublishBatch?> ConfirmAsync(PublishBatch batch)
        {
            if (await gitInfo is { } info)
            {
                console.MarkupLine(GitSummary.ToMarkup(info, DateTimeOffset.Now, maxWidth: console.Profile.Width - 1));
                console.WriteLine();
            }

            return !interactive || settings.Yes || confirmPage.Confirm(batch, "Cancel") ? batch : null;
        }
    }

    private async Task<List<PublishRequest>> CreateRequestsAsync(PublishBatch batch, Settings settings, bool interactive, CancellationToken cancellationToken)
    {
        var requests = new List<PublishRequest>();
        foreach (var (project, profile) in batch.Selections)
        {
            // A job that can't run here fails straight away, so don't ask for its password.
            // With --yes (or no terminal), Web Deploy uses the profile's app offline setting.
            var askAppOffline = interactive && !settings.Yes;
            requests.Add(new PublishRequest(project, profile)
            {
                TakeAppOffline = askAppOffline ? appOffline.Ask(project, profile, showProjectName: batch.Selections.Count > 1) : null,
                Configuration = settings.Configuration ?? batch.Configuration,
                Verbose = settings.Verbose,
                Password = runner.NeedsPassword(profile) ? await passwords.GetAsync(project, profile, allowPrompt: interactive, cancellationToken) : null,
                Git = await git.GetInfoAsync(project.Directory, cancellationToken: cancellationToken)
            });
        }

        return requests;
    }

    /// <summary>
    /// Ctrl+C while publishing cancels the jobs (and kills the build processes) instead of ending Pubwiz,
    /// so the results and logs can still be shown and exported. A second Ctrl+C exits right away.
    /// </summary>
    private async Task RunWithCtrlCAsync(IReadOnlyList<PublishJob> jobs, int parallel, Settings settings)
    {
        using var cancellation = new CancellationTokenSource();
        using (CtrlC.CancelInstead(cancellation))
        {
            var run = runner.RunAsync(jobs, parallel, cancellation.Token);
            await dashboard.ShowAsync(jobs, run, new DashboardOptions(settings.ShowLog, settings.LogLines));
        }
    }

    /// <summary>The --report and --log-dir exports, written without asking.</summary>
    private void WriteRequestedExports(IReadOnlyList<PublishJob> jobs, Settings settings)
    {
        if (settings.Report is { } report)
        {
            var reportPath = UserPath.ToFullPath(report);
            if (reportPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                PublishExporter.WriteCsvReport(jobs, reportPath);
            }
            else
            {
                PublishExporter.WriteJsonReport(jobs, reportPath);
            }

            console.MarkupLine($"Report: {DisplayText.Link(reportPath)}");
        }

        if (settings.LogDirectory is { } logDirectory)
        {
            var logFolder = UserPath.ToFullPath(logDirectory);
            var files = PublishExporter.WriteLogs(jobs, logFolder);
            console.MarkupLine($"Logs: {files.Count} file(s) in {DisplayText.Link(logFolder)}");
        }
    }

    private static int GetExitCode(IReadOnlyList<PublishJob> jobs)
    {
        if (jobs.All(j => j.Status == JobStatus.Succeeded))
        {
            return ExitCodes.Success;
        }

        return jobs.Any(j => j.Status == JobStatus.Failed) ? ExitCodes.PublishFailed : ExitCodes.Cancelled;
    }
}
