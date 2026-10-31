using Pubwiz.Core.Processes;
using System.Text;

namespace Pubwiz.Core.MsDeploy;

/// <summary>Thrown when msdeploy.exe fails, or can't be given a value.</summary>
public sealed class MsDeployException(string message) : Exception(message);

/// <summary>
/// Wrapper around <c>msdeploy.exe</c> (Windows only) for syncing a published app to an IIS server.
/// </summary>
/// <remarks>
/// msdeploy.exe has no way to read a password from stdin or a file, so the password is on its command line
/// and other users of this machine could see it in the process list while it runs.
/// </remarks>
/// <param name="processRunner">Runs the process.</param>
/// <param name="executablePath">From <see cref="MsDeployLocator.Find"/>; <c>null</c> when it isn't installed.</param>
public sealed class MsDeployCli(IProcessRunner processRunner, string? executablePath)
{
    public bool IsInstalled => executablePath is not null;

    /// <summary>
    /// Copies <see cref="MsDeploySyncOptions.SourceDirectory"/> to the IIS application, streaming msdeploy's output.
    /// </summary>
    /// <exception cref="MsDeployException">A value can't be passed to msdeploy, or the sync failed.</exception>
    /// <exception cref="PrerequisiteMissingException">msdeploy.exe isn't installed.</exception>
    public async Task SyncAsync(MsDeploySyncOptions options, Action<ProcessOutputLine> onOutput, CancellationToken cancellationToken)
    {
        var executable = executablePath
            ?? throw new PrerequisiteMissingException(MsDeployLocator.ExecutableName, MsDeployLocator.GetMissingReason() ?? string.Empty);

        var spec = new ProcessSpec(executable, []) { CommandLine = BuildSyncCommandLine(options) };
        var result = await processRunner.RunAsync(spec, onOutput, cancellationToken);
        if (!result.Succeeded)
        {
            throw new MsDeployException(DescribeFailure(result));
        }
    }

    /// <summary>
    /// The command line for <c>msdeploy -verb:sync</c>, e.g.
    /// <c>-verb:sync -source:iisApp="C:\app" -dest:iisApp="Default Web Site/api",computerName="https://...",authType="Basic" -allowUntrusted</c>.
    /// </summary>
    internal static string BuildSyncCommandLine(MsDeploySyncOptions options)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.SourceDirectory));
        var destination = options.Destination;

        var dest = new StringBuilder($"-dest:iisApp={Quote("IIS site", destination.IisPath)}")
            .Append($",computerName={Quote("server URL", destination.ServiceUrl.AbsoluteUri)}");
        if (options.UserName is not null)
        {
            dest.Append($",userName={Quote("user name", options.UserName)}");
            dest.Append($",password={Quote("password", options.Password ?? string.Empty)}");
        }

        dest.Append($",authType={Quote("auth type", options.EffectiveAuthType)}");

        List<string> arguments = ["-verb:sync", $"-source:iisApp={Quote("app folder", source)}", dest.ToString()];
        if (options.AllowUntrustedCertificate)
        {
            arguments.Add("-allowUntrusted");
        }

        if (options.KeepExtraFiles)
        {
            arguments.Add("-enableRule:DoNotDeleteRule");
        }

        if (options.TakeAppOffline)
        {
            arguments.Add("-enableRule:AppOffline");
        }

        if (options.Verbose)
        {
            arguments.Add("-verbose");
        }

        return string.Join(' ', arguments);
    }

    /// <summary>
    /// Wraps a provider setting value in double quotes, which keeps spaces and commas inside it.
    /// </summary>
    /// <remarks>
    /// msdeploy.exe splits its raw command line itself: it treats both <c>"</c> and <c>'</c> as quotes, has no escape
    /// for either inside a value, and reads a backslash before the closing quote as an escaped quote.
    /// Such values are rejected with a clear error instead of being sent in a form msdeploy would misread.
    /// </remarks>
    /// <param name="what">Names the value in the error message (never the value itself, which may be a password).</param>
    internal static string Quote(string what, string value)
    {
        if (value.IndexOfAny(['"', '\'']) >= 0)
        {
            throw new MsDeployException($"The {what} contains a quote character (\" or '), which msdeploy.exe can't receive.");
        }

        if (value.EndsWith('\\'))
        {
            throw new MsDeployException($"The {what} ends with a backslash, which msdeploy.exe can't receive.");
        }

        return $"\"{value}\"";
    }

    /// <summary>
    /// A one-line reason from msdeploy's output. Failures look like:
    /// <code>
    /// Error Code: ERROR_USER_UNAUTHORIZED
    /// More Information: Connected to the remote computer ... could not authorize. ...  Learn more at: https://...
    /// Error: The remote server returned an error: (401) Unauthorized.
    /// Error count: 1.
    /// </code>
    /// </summary>
    internal static string DescribeFailure(ProcessResult result)
    {
        var lines = (result.Error + "\n" + result.Output)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var code = ValueAfter(lines, "Error Code:");
        var information = ValueAfter(lines, "More Information:");
        if (information is not null)
        {
            var learnMore = information.IndexOf(" Learn more at:", StringComparison.Ordinal);
            information = (learnMore > 0 ? information[..learnMore] : information).Trim();
            return code is null ? information : $"{code}: {information}";
        }

        var error = lines.FirstOrDefault(line => line.StartsWith("Error:", StringComparison.Ordinal) && !line.StartsWith("Error count", StringComparison.Ordinal));
        return error?["Error:".Length..]?.Trim() ?? code ?? $"msdeploy.exe failed with exit code {result.ExitCode}";
    }

    private static string? ValueAfter(IEnumerable<string> lines, string prefix) =>
        lines.FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..]?.Trim();
}
