namespace Pubwiz.Core.Dotnet;

/// <summary>
/// Arguments for <c>dotnet publish</c>.
/// </summary>
/// <param name="ProjectPath">The project file to publish.</param>
public sealed record DotnetPublishOptions(string ProjectPath)
{
    public string Configuration { get; init; } = "Release";

    /// <summary>Publish profile name (<c>-p:PublishProfile=</c>); its properties are applied by MSBuild.</summary>
    public string? ProfileName { get; init; }

    public string? TargetFramework { get; init; }

    public string? RuntimeIdentifier { get; init; }

    /// <summary>Where to write the app (<c>-o</c>); <c>null</c> lets the project or profile decide.</summary>
    public string? OutputDirectory { get; init; }

    /// <summary>Extra MSBuild properties (<c>-p:Name=Value</c>).</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// MSBuild properties passed as environment variables instead of arguments, so values like passwords
    /// don't show up in process listings. MSBuild reads every environment variable as a property.
    /// </summary>
    public IReadOnlyDictionary<string, string> SecretProperties { get; init; } = new Dictionary<string, string>();

    /// <summary>MSBuild targets to run instead of the default <c>Publish</c>, e.g. <c>PublishContainer</c>.</summary>
    public IReadOnlyList<string> Targets { get; init; } = [];

    /// <summary>Normal instead of minimal MSBuild verbosity.</summary>
    public bool Verbose { get; init; }

    /// <summary>The command line arguments, without the dotnet executable.</summary>
    public IReadOnlyList<string> ToArguments()
    {
        List<string> arguments = ["publish", ProjectPath, "-c", Configuration];

        if (TargetFramework is not null)
        {
            arguments.AddRange(["-f", TargetFramework]);
        }

        if (RuntimeIdentifier is not null)
        {
            arguments.AddRange(["-r", RuntimeIdentifier]);
        }

        if (OutputDirectory is not null)
        {
            arguments.AddRange(["-o", OutputDirectory]);
        }

        if (ProfileName is not null)
        {
            arguments.Add($"-p:PublishProfile={ProfileName}");
        }

        arguments.AddRange(Targets.Select(t => $"-t:{t}"));

        arguments.AddRange(Properties.Select(p => $"-p:{p.Key}={p.Value}"));

        // -tl:off: the terminal logger's live view is useless when output is captured line by line.
        arguments.AddRange(["-nologo", "-tl:off", Verbose ? "-v:n" : "-v:m"]);
        return arguments;
    }
}
