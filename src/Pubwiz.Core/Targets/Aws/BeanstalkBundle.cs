using Pubwiz.Core.Publishing.Steps;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pubwiz.Core.Targets.Aws;

/// <summary>
/// Builds an Elastic Beanstalk source bundle from a publish folder, the way AWS's own .NET deploy tool does:
/// the app's files at the root of a zip, plus what the platform needs to start it.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Windows Server (IIS) runs .NET apps only with an <c>aws-windows-deployment-manifest.json</c> that lists them,
/// so one is added that runs the app at the site root, unless the app has its own.</item>
/// <item>The Linux .NET platform finds a framework-dependent app by itself; a self-contained one needs a
/// <c>Procfile</c> naming the executable.</item>
/// </list>
/// </remarks>
/// <seealso href="https://docs.aws.amazon.com/elasticbeanstalk/latest/dg/dotnet-manifest.html"/>
/// <seealso href="https://docs.aws.amazon.com/elasticbeanstalk/latest/dg/dotnet-linux-procfile.html"/>
public static class BeanstalkBundle
{
    public const string WindowsManifestName = "aws-windows-deployment-manifest.json";
    public const string ProcfileName = "Procfile";

    private const string RuntimeConfigSuffix = ".runtimeconfig.json";

    /// <summary>Zips <paramref name="publishDirectory"/> into <paramref name="zipPath"/> and adds the manifest or Procfile.</summary>
    /// <param name="publishDirectory">The <c>dotnet publish</c> output.</param>
    /// <param name="zipPath">The bundle to write.</param>
    /// <param name="windows"><c>true</c> for a Windows Server environment.</param>
    /// <returns>What was added, for the log, or <c>null</c> when nothing was needed.</returns>
    public static string? Create(string publishDirectory, string zipPath, bool windows)
    {
        ZipPackager.CreateFromDirectory(publishDirectory, zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);

        if (windows)
        {
            if (zip.GetEntry(WindowsManifestName) is not null)
            {
                return null;
            }

            WriteEntry(zip, WindowsManifestName, CreateWindowsManifest());
            return $"Added {WindowsManifestName} (runs the app at / in the Default Web Site)";
        }

        if (zip.GetEntry(ProcfileName) is not null || FindSelfContainedExecutable(publishDirectory) is not { } executable)
        {
            return null;
        }

        WriteEntry(zip, ProcfileName, $"web: ./{executable}");
        if (zip.GetEntry(executable) is { } entry)
        {
            ZipPackager.MarkExecutable(entry);
        }

        return $"Added a Procfile that starts ./{executable} (self-contained app)";
    }

    /// <summary>The manifest that runs the app in the root of IIS's Default Web Site.</summary>
    internal static string CreateWindowsManifest()
    {
        var manifest = new JsonObject
        {
            ["manifestVersion"] = 1,
            ["deployments"] = new JsonObject
            {
                ["aspNetCoreWeb"] = new JsonArray(new JsonObject
                {
                    ["name"] = "app",
                    ["parameters"] = new JsonObject
                    {
                        ["appBundle"] = ".",
                        ["iisPath"] = "/",
                        ["iisWebSite"] = "Default Web Site"
                    }
                })
            }
        };

        return manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// The app's executable when the publish is self-contained (its runtimeconfig lists <c>includedFrameworks</c>),
    /// otherwise <c>null</c>.
    /// </summary>
    internal static string? FindSelfContainedExecutable(string publishDirectory)
    {
        foreach (var runtimeConfig in Directory.EnumerateFiles(publishDirectory, "*" + RuntimeConfigSuffix))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(runtimeConfig));
            if (document.RootElement.TryGetProperty("runtimeOptions", out var options) && options.TryGetProperty("includedFrameworks", out _))
            {
                var fileName = Path.GetFileName(runtimeConfig);
                return fileName[..^RuntimeConfigSuffix.Length];
            }
        }

        return null;
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }
}
