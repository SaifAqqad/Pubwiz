using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Infrastructure.Processes;

namespace Pubwiz.Core.Publishing.Steps;

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
        var options = Options;

        // In parallel runs the job builds into its own folder, so builds of shared referenced projects don't collide.
        if (context.BuildDirectory is { } buildDirectory)
        {
            context.Log.Info($"Building in a separate folder, so parallel builds don't lock each other's files: {buildDirectory}");
            options = options with { ArtifactsPath = buildDirectory };
        }

        context.Log.Info($"dotnet {string.Join(' ', options.ToArguments())}");

        // Credentials from earlier steps (e.g. a registry sign-in) go in as environment variables.
        if (context.ToolEnvironment.Count > 0)
        {
            options = options with { SecretProperties = options.SecretProperties.Concat(context.ToolEnvironment).ToDictionary() };
        }

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
