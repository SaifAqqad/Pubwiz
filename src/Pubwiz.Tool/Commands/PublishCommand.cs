using Pubwiz.Core.Infrastructure.Git;
using Pubwiz.Core.Infrastructure.Platform;
using Pubwiz.Core.Presets;
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
    CertificateTrustPrompt certificates,
    PublishRunner runner,
    GitCli git,
    PublishDashboard dashboard,
    ResultsView results,
    RecentRunStore recentRuns) : AsyncCommand<PublishCommand.Settings>
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
        [Description("Publish this project (name or .csproj path) without the wizard; repeatable. Add :N for its order, e.g. Api:1 (the same number publishes at the same time). Needs --profile")]
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
        [Description("Show the live build output while publishing (toggle with L, switch project with ←/→)")]
        public bool ShowLog { get; init; }

        [DefaultValue(10)]
        [CommandOption("--log-lines")]
        [Description("How many lines of live output to show")]
        public int LogLines { get; init; }

        [CommandOption("--parallel")]
        [Description("The most projects to publish at the same time (default: all the projects of a step, or the preset's). 4 is recommended")]
        public int? Parallel { get; init; }

        [CommandOption("--no-app-offline")]
        [Description("Keep IIS apps running while publishing, instead of taking them offline with app_offline.htm (Web Deploy, FTP, SFTP)")]
        public bool NoAppOffline { get; init; }

        [CommandOption("--what-if")]
        [Description("Build, then only show what Web Deploy would change on the server, without deploying anything")]
        public bool WhatIf { get; init; }

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

            if (Profile is not null && Projects.Length == 0)
            {
                return ValidationResult.Error("--profile is used with --project: add --project <name> for each project to publish with it");
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

        // Listed by step, so the dashboard and results show the batch in the order it's published.
        var jobs = requests.OrderBy(r => r.Order).Select(runner.CreateJob).ToList();

        console.WriteLine();
        await RunWithCtrlCAsync(jobs, batch.Parallel ?? int.MaxValue, settings);

        // Remembered for "Repeat last publish" next time; not in CI, where nobody is asked.
        if (interactive && requests.Count > 0)
        {
            var projects = requests.Select(r => new PresetProject(r.Project.FilePath, r.Profile.Name, r.Order)).ToList();
            recentRuns.Save(GetStartPath(settings), new RecentRun(DateTimeOffset.Now, projects, settings.Configuration ?? batch.Configuration, batch.Parallel));
        }

        results.WriteResults(jobs);
        WriteRequestedExports(jobs, settings);
        if (interactive && !settings.Yes)
        {
            results.ShowMenu(jobs);
        }

        return GetExitCode(jobs);
    }

    /// <summary>The folder or file Pubwiz was started with, which the last publish is remembered for.</summary>
    private static string GetStartPath(Settings settings) => System.IO.Path.GetFullPath(settings.Path ?? Directory.GetCurrentDirectory());

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

        // Started now so it runs while the projects load. The fetch (for commits to pull) runs in the background and
        // updates the git line when it's done; it's skipped in CI.
        var gitStatus = GitStatus.Start(git, startDirectory, fetch: interactive, cancellationToken);

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

        // The last publish from here is offered at the top of the first list: the presets, or else the wizard's.
        var recent = recentRuns.Find(GetStartPath(settings));
        var offer = await presets.OfferAsync(startDirectory, recent, cancellationToken);
        if (offer.Batch is { } preset)
        {
            return await ConfirmAsync(preset);
        }

        // The wizard asks before publishing itself, since cancelling there goes back to its project list.
        // It shows the git line above its project list.
        return await wizard.RunAsync(new WizardOptions(path, settings.IncludeAll, Confirm: !settings.Yes, Git: gitStatus, Parallel: settings.Parallel,
            Recent: offer.Shown ? null : recent), cancellationToken);

        async Task<PublishBatch?> ConfirmAsync(PublishBatch batch)
        {
            // --parallel wins over the preset's setting; applied here so the confirm question shows what will happen.
            batch = batch with { Parallel = settings.Parallel ?? batch.Parallel };

            // Whatever is known now: the confirm question doesn't wait for the fetch.
            if (await gitStatus.GetLatestAsync() is { } info)
            {
                console.MarkupLine(GitSummary.ToMarkup(info, DateTimeOffset.Now, maxWidth: console.Profile.Width - 1));
                console.WriteLine();
            }

            return !interactive || settings.Yes ? batch : confirmPage.Confirm(batch, "Cancel");
        }
    }

    private async Task<List<PublishRequest>> CreateRequestsAsync(PublishBatch batch, Settings settings, bool interactive, CancellationToken cancellationToken)
    {
        var requests = new List<PublishRequest>();
        var steps = batch.Steps;
        foreach (var ((project, profile), step) in batch.Selections.Zip(steps))
        {
            // A job that can't run here fails straight away, so don't ask for its password.
            // With --yes (or no terminal), Web Deploy uses the profile's certificate setting.
            var ask = interactive && !settings.Yes;
            requests.Add(new PublishRequest(project, profile)
            {
                // Apps are taken offline unless --no-app-offline says otherwise (see PublishRequest.TakeAppOffline).
                Order = step,
                TakeAppOffline = settings.NoAppOffline ? false : null,
                WhatIf = settings.WhatIf,
                AllowUntrustedCertificate = ask ? await certificates.AskAsync(profile, cancellationToken) : null,
                Configuration = settings.Configuration ?? batch.Configuration,
                Verbose = settings.Verbose,
                Password = runner.NeedsPassword(profile) ? await passwords.GetAsync(project, profile, GetPasswordPrompt(interactive, settings), cancellationToken) : null,
                Git = await git.GetInfoAsync(project.Directory, cancellationToken: cancellationToken)
            });
        }

        return requests;
    }

    /// <summary>
    /// Without a terminal nothing is asked; with <c>--yes</c> only a missing password is; otherwise a saved password is
    /// confirmed too, so a stale one can be removed. (The wizard has asked already, and the answers are remembered.)
    /// </summary>
    private static PasswordPrompt GetPasswordPrompt(bool interactive, Settings settings) => (interactive, settings.Yes) switch
    {
        (false, _) => PasswordPrompt.Never,
        (true, true) => PasswordPrompt.WhenMissing,
        (true, false) => PasswordPrompt.Always
    };

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
