using Pubwiz.Core.Processes;

namespace Pubwiz.Core.Platform;

/// <summary>
/// Runs a command line through the system shell (<c>sh</c> or <c>cmd</c>), e.g. a suggested install command.
/// </summary>
public static class Shell
{
    /// <summary>Runs <paramref name="command"/> attached to the terminal and returns its exit code.</summary>
    public static Task<int> RunInteractiveAsync(IProcessRunner processRunner, string command, CancellationToken cancellationToken = default)
    {
        var spec = OperatingSystemInfo.IsWindows
            ? new ProcessSpec("cmd.exe", ["/c", command])
            : new ProcessSpec("/bin/sh", ["-c", command]);

        return processRunner.RunInteractiveAsync(spec, cancellationToken);
    }
}
