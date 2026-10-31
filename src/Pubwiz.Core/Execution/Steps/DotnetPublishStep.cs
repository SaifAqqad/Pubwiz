using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Logging;
using Pubwiz.Core.Processes;

namespace Pubwiz.Core.Execution.Steps;

/// <summary>
/// Builds and publishes the project with <c>dotnet publish</c>. Used by every target.
/// </summary>
/// <param name="dotnet">The dotnet CLI.</param>
/// <param name="options">The publish arguments for this run.</param>
/// <param name="outputDirectory">Where the published app ends up; stored in <see cref="PublishContext.OutputDirectory"/> for later steps.</param>
public sealed class DotnetPublishStep(DotnetCli dotnet, DotnetPublishOptions options, string outputDirectory) : PublishStep("Build and publish")
{
    /// <summary>How many build errors are shown in the step's error message.</summary>
    private const int MaxErrorsShown = 3;

    public DotnetPublishOptions Options { get; } = options;

    public override async Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
    {
        context.Log.Info($"dotnet {string.Join(' ', Options.ToArguments())}");

        // Credentials from earlier steps (e.g. a registry sign-in) go in as environment variables.
        var options = context.ToolEnvironment.Count == 0
            ? Options
            : Options with { SecretProperties = Options.SecretProperties.Concat(context.ToolEnvironment).ToDictionary() };

        var result = await dotnet.PublishAsync(options, line => OnOutput(context, line), cancellationToken);
        if (result.Succeeded)
        {
            context.OutputDirectory = outputDirectory;
            return;
        }

        var errors = DotnetCli.GetBuildErrors(result.Output);
        var message = errors.Count > 0
            ? string.Join(Environment.NewLine, errors.Take(MaxErrorsShown))
            : $"dotnet publish failed with exit code {result.ExitCode}";

        throw new PublishStepException(message);
    }

    private static void OnOutput(PublishContext context, ProcessOutputLine line)
    {
        if (string.IsNullOrWhiteSpace(line.Text))
        {
            return;
        }

        context.Log.Write(line.IsError ? LogSource.Error : LogSource.Output, line.Text);
        context.ReportDetail(line.Text.Trim());
    }
}
