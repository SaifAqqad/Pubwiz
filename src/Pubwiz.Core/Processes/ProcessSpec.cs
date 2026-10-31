namespace Pubwiz.Core.Processes;

/// <summary>
/// Describes an external process to run (dotnet, az, docker, ...).
/// </summary>
/// <param name="FileName">The executable name or full path.</param>
/// <param name="Arguments">Arguments passed as-is (no shell quoting needed).</param>
public sealed record ProcessSpec(string FileName, IReadOnlyList<string> Arguments)
{
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Extra environment variables for the process. A <c>null</c> value removes the variable.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Environment { get; init; } = new Dictionary<string, string?>();

    public override string ToString() => $"{FileName} {string.Join(' ', Arguments)}";
}
