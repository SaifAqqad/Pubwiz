using Microsoft.Web.XmlTransform;

namespace Pubwiz.Core.Transforms;

/// <summary>
/// Thrown when an XDT transform can't be applied.
/// </summary>
public sealed class TransformException(string message) : Exception(message);

/// <summary>
/// Applies XDT transforms (<c>xdt:Transform</c>/<c>xdt:Locator</c>) to a config file with Microsoft.Web.Xdt,
/// the library the .NET SDK uses during publish.
/// </summary>
public static class WebConfigTransformer
{
    /// <summary>
    /// Applies <paramref name="transforms"/> in order to <paramref name="sourcePath"/> and writes the result to <paramref name="outputPath"/>.
    /// The source file is never changed.
    /// </summary>
    /// <returns>Warnings reported by the transforms (e.g. a locator that matched nothing).</returns>
    /// <exception cref="TransformException">A transform failed.</exception>
    public static IReadOnlyList<string> Transform(string sourcePath, IReadOnlyList<string> transforms, string outputPath)
    {
        using var document = new XmlTransformableDocument { PreserveWhitespace = true };
        document.Load(sourcePath);

        var logger = new CollectingLogger();
        foreach (var transformPath in transforms)
        {
            using var transformation = new XmlTransformation(transformPath, logger);
            if (!transformation.Apply(document))
            {
                var details = logger.Errors.Count > 0 ? string.Join(Environment.NewLine, logger.Errors) : "no details";
                throw new TransformException($"Applying {Path.GetFileName(transformPath)} failed: {details}");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        document.Save(outputPath);
        return logger.Warnings;
    }

    /// <summary>Keeps the warnings and errors Microsoft.Web.Xdt reports.</summary>
    private sealed class CollectingLogger : IXmlTransformationLogger
    {
        public List<string> Warnings { get; } = [];

        public List<string> Errors { get; } = [];

        public void LogWarning(string message, params object[] messageArgs) => Warnings.Add(Format(message, messageArgs));

        public void LogWarning(string file, string message, params object[] messageArgs) => Warnings.Add($"{Path.GetFileName(file)}: {Format(message, messageArgs)}");

        public void LogWarning(string file, int lineNumber, int linePosition, string message, params object[] messageArgs) =>
            Warnings.Add($"{Path.GetFileName(file)}({lineNumber},{linePosition}): {Format(message, messageArgs)}");

        public void LogError(string message, params object[] messageArgs) => Errors.Add(Format(message, messageArgs));

        public void LogError(string file, string message, params object[] messageArgs) => Errors.Add($"{Path.GetFileName(file)}: {Format(message, messageArgs)}");

        public void LogError(string file, int lineNumber, int linePosition, string message, params object[] messageArgs) =>
            Errors.Add($"{Path.GetFileName(file)}({lineNumber},{linePosition}): {Format(message, messageArgs)}");

        public void LogErrorFromException(Exception ex) => Errors.Add(ex.Message);

        public void LogErrorFromException(Exception ex, string file) => Errors.Add($"{Path.GetFileName(file)}: {ex.Message}");

        public void LogErrorFromException(Exception ex, string file, int lineNumber, int linePosition) =>
            Errors.Add($"{Path.GetFileName(file)}({lineNumber},{linePosition}): {ex.Message}");

        // Informational messages aren't shown.
        public void LogMessage(string message, params object[] messageArgs) { }

        public void LogMessage(MessageType type, string message, params object[] messageArgs) { }

        public void StartSection(string message, params object[] messageArgs) { }

        public void StartSection(MessageType type, string message, params object[] messageArgs) { }

        public void EndSection(string message, params object[] messageArgs) { }

        public void EndSection(MessageType type, string message, params object[] messageArgs) { }

        private static string Format(string message, object[] args) => args.Length == 0 ? message : string.Format(message, args);
    }
}
