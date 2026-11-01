namespace Pubwiz.Tool.Infrastructure;

/// <summary>
/// Turns a path typed by the user (in a prompt or an option) into a full path.
/// </summary>
/// <remarks>
/// Shells expand <c>~</c> before Pubwiz sees it, but prompts, and PowerShell or cmd arguments, don't: without this,
/// <c>~/Desktop</c> would become a folder named <c>~</c> inside the current folder.
/// </remarks>
public static class UserPath
{
    /// <summary>
    /// Trims surrounding quotes and spaces, expands a leading <c>~</c> (<c>~</c>, <c>~/</c> or <c>~\</c>) to the home
    /// folder, and resolves the rest against the current folder.
    /// </summary>
    public static string ToFullPath(string path)
    {
        var trimmed = path.Trim().Trim('"', '\'');
        if (trimmed == "~")
        {
            return Home;
        }

        if (trimmed.StartsWith("~/", StringComparison.Ordinal) || trimmed.StartsWith("~\\", StringComparison.Ordinal))
        {
            return Path.GetFullPath(Path.Combine(Home, trimmed[2..]));
        }

        return Path.GetFullPath(trimmed);
    }

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
