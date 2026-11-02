using Pubwiz.Core.Infrastructure.Platform;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pubwiz.Core.Targets.Azure.StaticWebApps;

/// <summary>A build of StaticSitesClient, from Microsoft's release list.</summary>
/// <param name="Version"><c>stable</c> or <c>latest</c>.</param>
/// <param name="BuildId">The build, which names its download folder.</param>
/// <param name="Files">The download per platform (<c>linux-x64</c>, <c>win-x64</c>, <c>osx-x64</c>).</param>
public sealed record StaticSitesClientRelease(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("buildId")] string BuildId,
    [property: JsonPropertyName("publishDate")] string? PublishDate,
    [property: JsonPropertyName("files")] IReadOnlyDictionary<string, StaticSitesClientFile> Files);

/// <param name="Url">Where to download it.</param>
/// <param name="Sha">Its SHA-256 hash, in hex.</param>
public sealed record StaticSitesClientFile(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("sha")] string Sha);

/// <summary>
/// Gets <c>StaticSitesClient</c>, Microsoft's uploader for Azure Static Web Apps, the way the SWA CLI (<c>swa deploy</c>)
/// does: it reads the release list, downloads the stable build for this OS, checks its SHA-256 hash, and keeps it in
/// <c>~/.swa/deploy</c> with the same metadata file, so Pubwiz and the SWA CLI share one download.
/// </summary>
/// <seealso href="https://github.com/Azure/static-web-apps-cli/blob/main/src/core/deploy-client.ts"/>
public sealed class StaticSitesClientDownloader(HttpClient httpClient)
{
    public const string ReleaseListUrl = "https://aka.ms/swalocaldeploy";
    public const string StableVersion = "stable";
    private const string BinaryName = "StaticSitesClient";

    /// <summary>One download at a time, when several apps deploy in parallel.</summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>The folder the SWA CLI keeps the client in.</summary>
    public static string CacheDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".swa", "deploy");

    /// <summary>
    /// The client's platform name, or <c>null</c> when Microsoft doesn't build it for this machine. It's x64 only:
    /// Apple silicon Macs run it through Rosetta, but Linux on Arm can't.
    /// </summary>
    public static string? Platform =>
        OperatingSystemInfo.IsWindows ? "win-x64"
        : OperatingSystemInfo.IsMacOS ? "osx-x64"
        : OperatingSystemInfo.IsLinux && RuntimeInformation.OSArchitecture == Architecture.X64 ? "linux-x64"
        : null;

    /// <summary>The client's path, downloaded first when it's missing or a newer build is out.</summary>
    /// <param name="onStatus">Reports what's happening, e.g. "downloading StaticSitesClient".</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <exception cref="StaticWebAppException">It can't be downloaded or doesn't match its hash.</exception>
    public async Task<string> EnsureAsync(Action<string> onStatus, CancellationToken cancellationToken)
    {
        var platform = Platform ?? throw new StaticWebAppException($"StaticSitesClient isn't available for {OperatingSystemInfo.Rid}.");

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var cached = ReadCache();
            StaticSitesClientRelease? release;
            try
            {
                release = await GetStableReleaseAsync(cancellationToken);
            }
            catch (HttpRequestException) when (cached is not null && File.Exists(cached.Binary))
            {
                // Offline: the client from the last download still works.
                onStatus("using the downloaded StaticSitesClient (the release list can't be reached)");
                return cached.Binary;
            }

            var file = release.Files.GetValueOrDefault(platform) ?? throw new StaticWebAppException($"StaticSitesClient {release.BuildId} has no build for {platform}.");
            if (cached is not null && cached.Metadata.BuildId == release.BuildId && File.Exists(cached.Binary) && HashOf(cached.Binary) == file.Sha.ToLowerInvariant())
            {
                return cached.Binary;
            }

            onStatus($"downloading StaticSitesClient ({release.BuildId[..7]})");
            var binary = await DownloadAsync(release, file, cancellationToken);
            WriteCache(new CacheEntry(release, binary, file.Sha.ToLowerInvariant()));
            return binary;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<StaticSitesClientRelease> GetStableReleaseAsync(CancellationToken cancellationToken)
    {
        var releases = await httpClient.GetFromJsonAsync<List<StaticSitesClientRelease>>(ReleaseListUrl, cancellationToken) ?? [];
        return releases.FirstOrDefault(r => r.Version == StableVersion)
               ?? throw new StaticWebAppException($"The StaticSitesClient release list ({ReleaseListUrl}) has no {StableVersion} version.");
    }

    /// <summary>Downloads to a temporary file, checks the hash, then moves it into place.</summary>
    private async Task<string> DownloadAsync(StaticSitesClientRelease release, StaticSitesClientFile file, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(CacheDirectory, release.BuildId);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Path.GetFileName(new Uri(file.Url).AbsolutePath));
        var partial = path + ".download";

        using (var response = await httpClient.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new StaticWebAppException($"Downloading StaticSitesClient failed: {(int)response.StatusCode} {response.ReasonPhrase} ({file.Url})");
            }

            await using var output = File.Create(partial);
            await response.Content.CopyToAsync(output, cancellationToken);
        }

        var hash = HashOf(partial);
        if (hash != file.Sha.ToLowerInvariant())
        {
            File.Delete(partial);
            throw new StaticWebAppException($"The downloaded StaticSitesClient doesn't match its published hash (expected {file.Sha}, got {hash}), so it wasn't used.");
        }

        File.Move(partial, path, overwrite: true);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                                       | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        return path;
    }

    private static string HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>The SWA CLI's metadata file: which build was downloaded, where, and its hash.</summary>
    private sealed record CacheEntry(
        [property: JsonPropertyName("metadata")] StaticSitesClientRelease Metadata,
        [property: JsonPropertyName("binary")] string Binary,
        [property: JsonPropertyName("checksum")] string Checksum);

    private static string CacheFile => Path.Combine(CacheDirectory, $"{BinaryName}.json");

    private static CacheEntry? ReadCache()
    {
        try
        {
            return File.Exists(CacheFile) ? JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(CacheFile)) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WriteCache(CacheEntry entry) => File.WriteAllText(CacheFile, JsonSerializer.Serialize(entry));
}

/// <summary>Thrown when a Static Web Apps deployment can't go ahead.</summary>
public sealed class StaticWebAppException(string message) : Exception(message);
