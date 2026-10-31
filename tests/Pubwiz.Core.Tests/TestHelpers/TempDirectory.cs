namespace Pubwiz.Core.Tests.TestHelpers;

/// <summary>
/// A temporary folder that is deleted when disposed.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("pubwiz-tests-").FullName;

    /// <summary>
    /// Creates a file (and its parent folders) relative to the temp folder and returns its full path.
    /// </summary>
    public string CreateFile(string relativePath, string content = "")
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    /// <summary>
    /// Creates a minimal SDK-style project file.
    /// </summary>
    public string CreateProject(string relativePath, string sdk = "Microsoft.NET.Sdk", string properties = "<TargetFramework>net10.0</TargetFramework>")
    {
        return CreateFile(relativePath, $"""
            <Project Sdk="{sdk}">
              <PropertyGroup>
                {properties}
              </PropertyGroup>
            </Project>
            """);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp folder doesn't fail a test.
        }
    }
}
