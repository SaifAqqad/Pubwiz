using Pubwiz.Core.Logging;

namespace Pubwiz.Core.Execution;

/// <summary>
/// Shared state of a running job, passed to every step. Earlier steps store their results here
/// (e.g. the publish folder) for later steps to use.
/// </summary>
public sealed class PublishContext(PublishJob job)
{
    public PublishRequest Request => job.Request;

    public PublishLog Log => job.Log;

    /// <summary>The folder <c>dotnet publish</c> wrote the app to.</summary>
    public string? OutputDirectory
    {
        get => job.OutputDirectory;
        set => job.OutputDirectory = value;
    }

    /// <summary>The package built from the publish folder (zip, image tarball, ...), if any.</summary>
    public string? PackagePath
    {
        get => job.PackagePath;
        set => job.PackagePath = value;
    }

    /// <summary>Where the app ended up: a folder, URL or image name, shown in the results.</summary>
    public string? Destination
    {
        get => job.Destination;
        set => job.Destination = value;
    }

    /// <summary>
    /// Environment variables for the external tools later steps run, e.g. registry credentials that a sign-in step
    /// gets and the container publish step needs. Values are secrets: they're masked in the log.
    /// </summary>
    public Dictionary<string, string> ToolEnvironment { get; } = new();

    /// <summary>Adds an environment variable for later tool runs and masks its value in the log.</summary>
    public void SetToolEnvironment(string name, string value)
    {
        ToolEnvironment[name] = value;
        Log.AddSecret(value);
    }

    /// <summary>Updates the running step's live status line.</summary>
    public void ReportDetail(string? detail)
    {
        if (job.CurrentStep is { } step)
        {
            step.Detail = detail;
        }
    }

    /// <summary>Updates the running step's progress (0 to 1).</summary>
    public void ReportProgress(double progress)
    {
        if (job.CurrentStep is { } step)
        {
            step.Progress = Math.Clamp(progress, 0, 1);
        }
    }
}
