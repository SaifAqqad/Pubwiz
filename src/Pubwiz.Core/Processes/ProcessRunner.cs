using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Pubwiz.Core.Processes;

/// <summary>
/// Default <see cref="IProcessRunner"/> built on <see cref="Process"/>.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessSpec spec, Action<ProcessOutputLine>? onOutput = null, CancellationToken cancellationToken = default)
    {
        using var process = new Process { StartInfo = BuildStartInfo(spec) };

        var output = new StringBuilder();
        var error = new StringBuilder();

        // Both streams are read through events so a process that fills one pipe can't block on the other.
        process.OutputDataReceived += (_, e) => OnLine(e.Data, isError: false);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data, isError: true);

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new ProcessStartException(spec.FileName, ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);
            throw;
        }

        return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());

        void OnLine(string? line, bool isError)
        {
            // A null line means the stream was closed.
            if (line is null)
            {
                return;
            }

            var buffer = isError ? error : output;
            lock (buffer)
            {
                buffer.AppendLine(line);
            }

            onOutput?.Invoke(new ProcessOutputLine(line, isError));
        }
    }

    private static ProcessStartInfo BuildStartInfo(ProcessSpec spec)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = spec.FileName,
            WorkingDirectory = spec.WorkingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in spec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in spec.Environment)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(name);
            }
            else
            {
                startInfo.Environment[name] = value;
            }
        }

        return startInfo;
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process already exited.
        }
    }
}
