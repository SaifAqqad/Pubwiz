using Pubwiz.Core.Execution;
using Pubwiz.Tool.Credentials;
using Pubwiz.Tool.Rendering;
using Pubwiz.Tool.Wizard;
using System.ComponentModel;

namespace Pubwiz.Tool.Commands;

/// <summary>
/// The default command: finds projects, walks the user through choosing profiles, then publishes.
/// </summary>
public sealed class PublishCommand(
    IAnsiConsole console,
    PublishWizard wizard,
    PasswordProvider passwords,
    PublishRunner runner,
    PublishDashboard dashboard,
    ResultsView results) : AsyncCommand<PublishCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[path]")]
        [Description("Folder, solution (.sln/.slnx) or project (.csproj) to publish. Defaults to the current folder")]
        public string? Path { get; init; }

        [CommandOption("-c|--configuration")]
        [Description("Build configuration, overriding the profile's (e.g. Release or Debug)")]
        public string? Configuration { get; init; }

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

        [DefaultValue(1)]
        [CommandOption("--parallel")]
        [Description("How many projects to publish at the same time")]
        public int Parallel { get; init; }

        [CommandOption("--verbose")]
        [Description("Use detailed build output")]
        public bool Verbose { get; init; }

        public override ValidationResult Validate()
        {
            if (LogLines < 1)
            {
                return ValidationResult.Error("--log-lines must be at least 1");
            }

            return Parallel < 1 ? ValidationResult.Error("--parallel must be at least 1") : ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Widgets.WriteHeader();

        var options = new WizardOptions(settings.Path ?? Directory.GetCurrentDirectory(), settings.IncludeAll);
        var selections = await wizard.RunAsync(options, cancellationToken);

        var requests = await CreateRequestsAsync(selections, settings, cancellationToken);
        var jobs = requests.Select(runner.CreateJob).ToList();

        console.WriteLine();
        if (!console.Confirm($"Publish {jobs.Count} project(s) now?"))
        {
            return ExitCodes.Cancelled;
        }

        console.WriteLine();
        await RunWithCtrlCAsync(jobs, settings);

        results.WriteResults(jobs);
        if (console.Profile.Capabilities.Interactive)
        {
            results.ShowMenu(jobs);
        }

        return GetExitCode(jobs);
    }

    private async Task<List<PublishRequest>> CreateRequestsAsync(IReadOnlyList<PublishSelection> selections, Settings settings, CancellationToken cancellationToken)
    {
        var requests = new List<PublishRequest>();
        foreach (var (project, profile) in selections)
        {
            var needsPassword = runner.FindTarget(profile)?.RequiresPassword(profile) ?? false;
            requests.Add(new PublishRequest(project, profile)
            {
                Configuration = settings.Configuration,
                Verbose = settings.Verbose,
                Password = needsPassword ? await passwords.GetAsync(project, profile, cancellationToken) : null
            });
        }

        return requests;
    }

    /// <summary>
    /// Ctrl+C while publishing cancels the jobs (and kills the build processes) instead of ending Pubwiz,
    /// so the results and logs can still be shown and exported. A second Ctrl+C exits right away.
    /// </summary>
    private async Task RunWithCtrlCAsync(IReadOnlyList<PublishJob> jobs, Settings settings)
    {
        using var cancellation = new CancellationTokenSource();

        void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = !cancellation.IsCancellationRequested;
            cancellation.Cancel();
        }

        Console.CancelKeyPress += OnCancelKeyPress;
        try
        {
            var run = runner.RunAsync(jobs, settings.Parallel, cancellation.Token);
            await dashboard.ShowAsync(jobs, run, new DashboardOptions(settings.ShowLog, settings.LogLines));
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
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
