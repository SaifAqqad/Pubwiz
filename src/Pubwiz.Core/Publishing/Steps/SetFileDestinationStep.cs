namespace Pubwiz.Core.Publishing.Steps;

/// <summary>
/// Like <see cref="SetDestinationStep"/>, for targets that produce one file (a zip, a container tarball):
/// records the full path of the file that was actually written.
/// </summary>
/// <remarks>
/// When <paramref name="expectedPath"/> turns out to be a folder (a tool may write <c>{name}.tar.gz</c> inside it),
/// the newest matching file in it is used instead.
/// </remarks>
/// <param name="expectedPath">The full path the file should be at.</param>
/// <param name="extensions">What the file can end with, e.g. <c>.zip</c> or <c>.tar.gz</c>, for the folder case.</param>
public sealed class SetFileDestinationStep(string expectedPath, params string[] extensions) : PublishStep("Done")
{
    public override Task ExecuteAsync(PublishContext context, CancellationToken cancellationToken)
    {
        var path = Resolve(expectedPath, extensions);
        if (!File.Exists(path))
        {
            context.Log.Write(LogSource.Error, $"Expected the output at {path}, but it isn't there");
        }

        context.Destination = path;
        context.Log.Info($"Wrote {path}");
        return Task.CompletedTask;
    }

    internal static string Resolve(string expectedPath, IReadOnlyList<string> extensions)
    {
        var fullPath = Path.GetFullPath(expectedPath);
        if (File.Exists(fullPath) || !Directory.Exists(fullPath))
        {
            return fullPath;
        }

        return new DirectoryInfo(fullPath).EnumerateFiles()
                   .Where(file => extensions.Any(extension => file.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
                   .OrderByDescending(file => file.LastWriteTimeUtc)
                   .FirstOrDefault()?.FullName
               ?? fullPath;
    }
}
