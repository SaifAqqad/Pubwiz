
namespace Pubwiz.Core.Publishing;

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
    /// The job's own build folder when jobs run in parallel (see <see cref="BuildIsolation"/>), or <c>null</c> to build
    /// in the projects' usual bin and obj folders.
    /// </summary>
    public string? BuildDirectory { get; init; }

    /// <summary>
    /// Environment variables for the external tools later steps run, e.g. registry credentials that a sign-in step
    /// gets and the container publish step needs.
    /// </summary>
    public Dictionary<string, string> ToolEnvironment { get; } = new();

    /// <summary>Adds an environment variable for later tool runs.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">Its value.</param>
    /// <param name="isSecret">Mask the value in the log (passwords, tokens; not user names).</param>
    public void SetToolEnvironment(string name, string value, bool isSecret)
    {
        ToolEnvironment[name] = value;
        if (isSecret)
        {
            Log.AddSecret(value);
        }
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
