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

    /// <summary>
    /// Text written to the process's standard input, which is then closed. Used to pass secrets
    /// (e.g. <c>secret-tool store</c>, <c>docker login --password-stdin</c>) without putting them on the command line.
    /// </summary>
    public string? StandardInput { get; init; }

    /// <summary>
    /// A ready-made command line, used instead of <see cref="Arguments"/> for tools that parse the raw command line
    /// themselves and reject the standard quoting (msdeploy.exe).
    /// </summary>
    public string? CommandLine { get; init; }

    public override string ToString() => $"{FileName} {CommandLine ?? string.Join(' ', Arguments)}";
}
