namespace Pubwiz.Core.Transfer;

/// <summary>
/// The <c>app_offline.htm</c> file: while it exists in an IIS app's root, the app is stopped and this page is shown.
/// </summary>
public static class AppOffline
{
    public const string FileName = "app_offline.htm";

    public const string Content = """
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"><title>Updating</title></head>
        <body><p>This site is being updated and will be back in a moment.</p></body>
        </html>
        """;
}
