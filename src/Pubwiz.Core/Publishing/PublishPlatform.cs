using Pubwiz.Core.Discovery;
using Pubwiz.Core.Infrastructure.Platform;
using Pubwiz.Core.Profiles;

namespace Pubwiz.Core.Publishing;

/// <summary>The operating system an app is published for.</summary>
public enum PublishOs
{
    Windows,
    Linux,
    MacOS
}

/// <summary>
/// What a target says about the platform of the apps it publishes (see <see cref="IPublishTarget.GetPlatform"/>).
/// </summary>
/// <param name="Applies"><c>false</c> when the target doesn't build for an OS (Static Web Apps, MAUI, Dockerfile builds).</param>
/// <param name="Os">The OS the target runs apps on (IIS: Windows), or <c>null</c> when it can't tell (a folder, FTP).</param>
/// <param name="Architecture">
/// The architecture to build for along with <see cref="Os"/> unless one is chosen, e.g. <c>x64</c>; <c>null</c> for
/// this machine's.
/// </param>
public sealed record TargetPlatform(bool Applies, PublishOs? Os = null, string? Architecture = PublishPlatform.ServerArchitecture)
{
    /// <summary>The target can't tell: the app is built for <c>--os</c> or the choice on the confirm page, else this machine.</summary>
    public static TargetPlatform Any { get; } = new(Applies: true);

    /// <summary>The target doesn't build for an OS.</summary>
    public static TargetPlatform None { get; } = new(Applies: false);

    /// <summary>The target runs apps on <paramref name="os"/> (on x64 unless <paramref name="architecture"/> says otherwise).</summary>
    public static TargetPlatform Of(PublishOs os, string? architecture = PublishPlatform.ServerArchitecture) => new(Applies: true, os, architecture);
}

/// <summary>Where a request's OS came from.</summary>
public enum PlatformSource
{
    /// <summary>The platform doesn't apply (Static Web Apps, MAUI, Blazor WebAssembly, Dockerfile builds).</summary>
    None,

    /// <summary>The profile's own <c>RuntimeIdentifier</c>.</summary>
    Profile,

    /// <summary>The target decides it, e.g. Windows for IIS.</summary>
    Target,

    /// <summary><c>--os</c>, a preset, or the choice on the confirm page.</summary>
    Chosen,

    /// <summary>Nothing decides it, so it's this machine's (what <c>dotnet publish</c> does by default).</summary>
    ThisMachine
}

/// <summary>The platform a request is published for.</summary>
/// <param name="Source">Where the OS came from.</param>
/// <param name="Os">The OS, or <c>null</c> when the platform doesn't apply or the profile's runtime has no known OS.</param>
/// <param name="Architecture">e.g. <c>x64</c>; <c>null</c> when the platform doesn't apply or the profile's runtime has none.</param>
/// <param name="RuntimeIdentifier">What's passed as <c>-r</c>, or <c>null</c> to let dotnet publish decide.</param>
public sealed record ResolvedPlatform(PlatformSource Source, PublishOs? Os, string? Architecture, string? RuntimeIdentifier)
{
    /// <summary>Whether <c>--os</c> or the confirm page decides the OS (neither the profile nor the target does).</summary>
    public bool CanChooseOs => Source is PlatformSource.Chosen or PlatformSource.ThisMachine;

    /// <summary>Whether <c>--arch</c> or the confirm page can change the architecture (the profile doesn't decide it).</summary>
    public bool CanChooseArchitecture => Source is not (PlatformSource.None or PlatformSource.Profile);

    /// <summary>e.g. "Linux x64", or the profile's runtime identifier when its OS isn't one of ours; <c>null</c> when it doesn't apply.</summary>
    public string? DisplayName => Source switch
    {
        PlatformSource.None => null,
        _ when Os is { } os && Architecture is { } architecture => $"{PublishPlatform.DisplayName(os)} {architecture}",
        _ => RuntimeIdentifier
    };
}

/// <summary>
/// Decides which OS and architecture an app is built for.
/// </summary>
/// <remarks>
/// The OS is the profile's runtime when it has one, then the target's (IIS runs on Windows, an Azure app on Linux or
/// Windows, containers on Linux), then the OS chosen with <c>--os</c> or on the confirm page, then this machine's.
/// The architecture is the profile's, then the chosen one (<c>--arch</c>), then x64 for servers and other OSes, or
/// this machine's for its own OS.
/// <para>
/// Without this, <c>dotnet publish</c> builds for the machine it runs on: an app published from a Mac to IIS gets a
/// macOS app host, and a self-contained one a macOS runtime.
/// </para>
/// </remarks>
public static class PublishPlatform
{
    /// <summary>The architecture of servers, Azure and container hosts, like Visual Studio assumes.</summary>
    public const string ServerArchitecture = "x64";

    /// <summary>The architectures .NET publishes for, by how common they are.</summary>
    public static IReadOnlyList<string> Architectures { get; } = ["x64", "arm64", "x86", "arm"];

    /// <summary>The OS of this machine; <c>null</c> on an OS .NET apps can't be published for.</summary>
    public static PublishOs? ThisMachine { get; } = ParseOs(OperatingSystemInfo.OsIdentifier);

    /// <param name="project">The project.</param>
    /// <param name="profile">Its profile, whose <c>RuntimeIdentifier</c> wins.</param>
    /// <param name="target">What the target says (see <see cref="IPublishTarget.GetPlatform"/>).</param>
    /// <param name="chosenOs">The OS from <c>--os</c> or the confirm page, used when the target doesn't decide it.</param>
    /// <param name="chosenArchitecture">The architecture from <c>--arch</c> or the confirm page, e.g. <c>arm64</c>.</param>
    public static ResolvedPlatform Resolve(ProjectInfo project, PublishProfile profile, TargetPlatform target, PublishOs? chosenOs,
        string? chosenArchitecture = null)
    {
        if (profile[ProfileProperties.RuntimeIdentifier] is { Length: > 0 } profileRid)
        {
            return new ResolvedPlatform(PlatformSource.Profile, OsOfRuntime(profileRid), ArchitectureOfRuntime(profileRid), profileRid);
        }

        // Blazor WebAssembly apps always run in the browser (browser-wasm).
        if (!target.Applies || project.Kind == ProjectKind.BlazorWasm)
        {
            return new ResolvedPlatform(PlatformSource.None, null, null, null);
        }

        if (target.Os is { } targetOs)
        {
            return Create(PlatformSource.Target, targetOs, chosenArchitecture ?? target.Architecture ?? OperatingSystemInfo.Architecture);
        }

        if (chosenOs is { } os)
        {
            // This machine's architecture for its own OS (an Apple silicon Mac publishing for macOS), else a server's.
            return Create(PlatformSource.Chosen, os, chosenArchitecture ?? (os == ThisMachine ? OperatingSystemInfo.Architecture : ServerArchitecture));
        }

        if (chosenArchitecture is not null && ThisMachine is { } thisMachine)
        {
            return Create(PlatformSource.ThisMachine, thisMachine, chosenArchitecture);
        }

        // No -r: dotnet publish builds for this machine, and keeps the native libraries of every OS.
        return new ResolvedPlatform(PlatformSource.ThisMachine, ThisMachine, OperatingSystemInfo.Architecture, null);

        static ResolvedPlatform Create(PlatformSource source, PublishOs os, string architecture) =>
            new(source, os, architecture, ToRuntimeIdentifier(os, architecture));
    }

    /// <summary>e.g. <c>linux-x64</c>; <paramref name="architecture"/> <c>null</c> is this machine's.</summary>
    public static string ToRuntimeIdentifier(PublishOs os, string? architecture) =>
        $"{ToIdentifier(os)}-{architecture ?? OperatingSystemInfo.Architecture}";

    /// <summary>The OS part of a runtime identifier: <c>win</c>, <c>linux</c> or <c>osx</c>.</summary>
    public static string ToIdentifier(PublishOs os) => os switch
    {
        PublishOs.Windows => "win",
        PublishOs.Linux => "linux",
        _ => "osx"
    };

    public static string DisplayName(PublishOs os) => os switch
    {
        PublishOs.Windows => "Windows",
        PublishOs.Linux => "Linux",
        _ => "macOS"
    };

    /// <summary>
    /// Reads an OS name as given to <c>--os</c> or in a preset: <c>win</c>/<c>windows</c>, <c>linux</c>, or
    /// <c>osx</c>/<c>macos</c> (any case).
    /// </summary>
    /// <returns>The OS, or <c>null</c> when the name isn't one of these.</returns>
    public static PublishOs? ParseOs(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "win" or "windows" => PublishOs.Windows,
        "linux" => PublishOs.Linux,
        "osx" or "macos" => PublishOs.MacOS,
        _ => null
    };

    /// <summary>
    /// Reads an architecture as given to <c>--arch</c> or in a preset: <c>x64</c>, <c>arm64</c>, <c>x86</c> or
    /// <c>arm</c>, also by their other common names (<c>amd64</c>, <c>x86_64</c>, <c>aarch64</c>, any case).
    /// </summary>
    /// <returns>The runtime identifier's spelling, or <c>null</c> when the name isn't an architecture .NET publishes for.</returns>
    public static string? ParseArchitecture(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "x64" or "amd64" or "x86_64" or "x86-64" => "x64",
        "arm64" or "aarch64" => "arm64",
        "x86" or "i386" or "i686" => "x86",
        "arm" or "arm32" or "armhf" => "arm",
        _ => null
    };

    /// <summary>
    /// Reads <c>--os</c>: an OS (<c>linux</c>), or an OS with its architecture (<c>linux-arm64</c>, like a runtime identifier).
    /// </summary>
    /// <returns><c>false</c> when it's neither.</returns>
    public static bool TryParsePlatform(string value, out PublishOs os, out string? architecture)
    {
        architecture = null;
        var parts = value.Trim().Split('-', 2);
        if (ParseOs(parts[0]) is not { } parsed)
        {
            os = default;
            return false;
        }

        os = parsed;
        if (parts.Length == 1)
        {
            return true;
        }

        architecture = ParseArchitecture(parts[1]);
        return architecture is not null;
    }

    /// <summary>
    /// The OS of a runtime identifier: <c>win-x64</c> → Windows, <c>linux-musl-arm64</c> → Linux, <c>osx-arm64</c> →
    /// macOS; <c>null</c> for others (<c>ios-arm64</c>, <c>browser-wasm</c>, distribution-specific ones).
    /// </summary>
    public static PublishOs? OsOfRuntime(string runtimeIdentifier) =>
        runtimeIdentifier.Split('-', 2)[0].ToLowerInvariant() switch
        {
            "win" or "win7" or "win8" or "win81" or "win10" => PublishOs.Windows,
            "linux" => PublishOs.Linux,
            "osx" => PublishOs.MacOS,
            _ => null
        };

    /// <summary>The architecture at the end of a runtime identifier (<c>linux-musl-arm64</c> → <c>arm64</c>), or <c>null</c>.</summary>
    public static string? ArchitectureOfRuntime(string runtimeIdentifier)
    {
        var last = runtimeIdentifier[(runtimeIdentifier.LastIndexOf('-') + 1)..];
        return Architectures.Contains(last, StringComparer.OrdinalIgnoreCase) ? last.ToLowerInvariant() : null;
    }
}
