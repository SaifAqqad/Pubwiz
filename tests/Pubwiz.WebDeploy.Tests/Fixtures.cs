namespace Pubwiz.WebDeploy.Tests;

/// <summary>Helpers for the binary fixtures captured from the official Web Deploy client.</summary>
internal static class Fixtures
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(Root, name));
}
