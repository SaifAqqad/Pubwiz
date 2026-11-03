using Pubwiz.Core.Publishing;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Pubwiz.Core.Targets.FileTransfer;

/// <summary>
/// Runs a command on the server over SSH, e.g. stopping a service before uploading and starting it again afterwards.
/// The command goes to the server's own shell as written: bash or sh on Linux, cmd or PowerShell on Windows
/// (whichever the OpenSSH server is set up with). A non-zero exit code fails the step.
/// </summary>
/// <param name="name">The step name, e.g. "Run pre-deploy command".</param>
/// <param name="getConnection">Gets the same server and login the upload uses, when the step runs.</param>
/// <param name="command">The command line to run.</param>
public sealed class SshCommandStep(string name, Func<PublishContext, CancellationToken, Task<SftpConnection>> getConnection, string command) : PublishStep(name)
{
    public string Command => command;

    public override async Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
    {
        var connection = await getConnection(context, cancellationToken);
        using var client = new SshClient(connection.ToConnectionInfo());
        context.ReportDetail("connecting...");
        try
        {
            await client.ConnectAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is SshException or System.Net.Sockets.SocketException or IOException)
        {
            throw new PublishStepException($"Could not connect to ssh://{connection.Host}:{connection.Port}: {ex.Message}", ex);
        }

        context.Log.Info($"$ {command}");
        context.ReportDetail(command);

        using var sshCommand = client.CreateCommand(command);
        var execution = sshCommand.ExecuteAsync(cancellationToken);

        // Both streams are read while the command runs, so its output shows up live.
        string? lastError = null;
        await Task.WhenAll(
            ReadLinesAsync(sshCommand.OutputStream, line => WriteOutput(context, line)),
            ReadLinesAsync(sshCommand.ExtendedOutputStream, line =>
            {
                lastError = line;
                WriteOutput(context, line);
            }));

        try
        {
            await execution;
        }
        catch (SshException ex)
        {
            throw new PublishStepException($"The command failed: {ex.Message}", ex);
        }

        client.Disconnect();

        if (sshCommand.ExitStatus is not 0)
        {
            var reason = sshCommand.ExitSignal is { } signal ? $"was stopped by signal {signal}" : $"exited with code {sshCommand.ExitStatus}";
            var detail = lastError is null ? string.Empty : $": {lastError}";
            throw new PublishStepException($"'{command}' {reason}{detail}");
        }
    }

    private static async Task ReadLinesAsync(Stream stream, Action<string> onLine)
    {
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                onLine(line.TrimEnd());
            }
        }
    }

    private static void WriteOutput(PublishContext context, string line)
    {
        // Many tools write progress to stderr, so both streams are logged as normal output.
        context.Log.Write(LogSource.Output, line);
        context.ReportDetail(line.Trim());
    }
}
