using Pubwiz.Core.Infrastructure;
using Pubwiz.Core.Infrastructure.Platform;
using Pubwiz.Core.Infrastructure.Processes;

namespace Pubwiz.Core.Targets.Containers;

/// <summary>The local container tools Pubwiz can drive.</summary>
public enum ContainerTool
{
    Docker,
    Podman
}

/// <summary>Thrown when a docker/podman command fails.</summary>
public sealed class ContainerCliException(string message) : Exception(message);

/// <summary>
/// Wrapper around <c>docker</c> or <c>podman</c> (same commands) for the Dockerfile builder, logins, pushes and saves.
/// </summary>
public sealed class ContainerCli(IProcessRunner processRunner)
{
    /// <summary>
    /// The tools that are installed and whose daemon/service answers (<c>info</c> succeeds), Docker first.
    /// </summary>
    public async Task<IReadOnlyList<ContainerTool>> DetectAsync(CancellationToken cancellationToken = default)
    {
        var available = new List<ContainerTool>();
        foreach (var tool in Enum.GetValues<ContainerTool>())
        {
            if (ExecutableLocator.FindInPath(CommandName(tool)) is { } path
                && (await processRunner.RunAsync(new ProcessSpec(path, ["info"]), cancellationToken: cancellationToken)).Succeeded)
            {
                available.Add(tool);
            }
        }

        return available;
    }

    /// <summary><c>build -f Dockerfile -t image:tag ... context</c>, streaming the build output.</summary>
    public async Task BuildAsync(ContainerTool tool, string dockerfile, string context, IReadOnlyList<string> imageNames, Action<ProcessOutputLine> onOutput, CancellationToken cancellationToken)
    {
        List<string> arguments = ["build", "-f", dockerfile];
        foreach (var image in imageNames)
        {
            arguments.AddRange(["-t", image]);
        }

        arguments.Add(context);
        await RunAsync(tool, arguments, stdin: null, onOutput, cancellationToken);
    }

    public Task PushAsync(ContainerTool tool, string imageName, Action<ProcessOutputLine> onOutput, CancellationToken cancellationToken) =>
        RunAsync(tool, ["push", imageName], stdin: null, onOutput, cancellationToken);

    /// <summary><c>save -o file image...</c>: writes the images to a tarball.</summary>
    public Task SaveAsync(ContainerTool tool, IReadOnlyList<string> imageNames, string outputPath, Action<ProcessOutputLine> onOutput, CancellationToken cancellationToken) =>
        RunAsync(tool, ["save", "-o", outputPath, .. imageNames], stdin: null, onOutput, cancellationToken);

    /// <summary>Signs in to a registry; the password goes through stdin so it isn't visible in the process list.</summary>
    public Task LoginAsync(ContainerTool tool, string registry, string userName, string password, CancellationToken cancellationToken) =>
        RunAsync(tool, ["login", registry, "--username", userName, "--password-stdin"], password, onOutput: null, cancellationToken);

    private async Task RunAsync(ContainerTool tool, IReadOnlyList<string> arguments, string? stdin, Action<ProcessOutputLine>? onOutput, CancellationToken cancellationToken)
    {
        var executable = ExecutableLocator.FindInPath(CommandName(tool))
            ?? throw new PrerequisiteMissingException(CommandName(tool), $"Install {tool} or pick another container tool.");

        var result = await processRunner.RunAsync(new ProcessSpec(executable, arguments) { StandardInput = stdin }, onOutput, cancellationToken);
        if (!result.Succeeded)
        {
            var lastError = result.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
            throw new ContainerCliException($"{CommandName(tool)} {arguments[0]} failed: {lastError ?? $"exit code {result.ExitCode}"}");
        }
    }

    public static string CommandName(ContainerTool tool) => tool == ContainerTool.Docker ? "docker" : "podman";
}
