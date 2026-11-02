namespace Pubwiz.WebDeploy;

/// <summary>What a <see cref="WebDeploySkipRule"/> matches.</summary>
public enum WebDeploySkipObject
{
    /// <summary>Files (msdeploy's <c>filePath</c>).</summary>
    File,

    /// <summary>Folders (msdeploy's <c>dirPath</c>), with everything in them.</summary>
    Directory
}

/// <summary>The one operation a <see cref="WebDeploySkipRule"/> prevents, instead of skipping the object entirely.</summary>
public enum WebDeploySkipAction
{
    /// <summary>Never delete matching objects on the server (they're still added and updated).</summary>
    Delete,

    /// <summary>Never update matching objects that already exist on the server.</summary>
    Update,

    /// <summary>Never add matching objects to the server.</summary>
    AddChild
}

/// <summary>
/// Leaves matching files or folders alone during a sync, like msdeploy's <c>-skip</c> argument
/// (<c>-skip:objectName=dirPath,absolutePath=\\uploads$</c>).
/// </summary>
/// <param name="Object">Whether the rule matches files or folders.</param>
/// <param name="AbsolutePath">
/// A regular expression matched against the object's full path, e.g. <c>\\uploads$</c> for a folder named
/// <c>uploads</c> or <c>\.log$</c> for log files. It can't contain a double quote.
/// </param>
public sealed record WebDeploySkipRule(WebDeploySkipObject Object, string AbsolutePath)
{
    /// <summary>
    /// The only operation to skip, e.g. <see cref="WebDeploySkipAction.Delete"/> to keep server-side files such as uploads
    /// or logs. <c>null</c> (the default) skips matching objects entirely: they're neither added, updated nor deleted.
    /// </summary>
    public WebDeploySkipAction? Action { get; init; }

    /// <summary>The <c>objectName</c> msdeploy uses for <see cref="Object"/>.</summary>
    internal string ObjectName => Object == WebDeploySkipObject.File ? "filePath" : "dirPath";

    /// <summary>
    /// Why <paramref name="absolutePath"/> can't be used as a skip rule path, or <c>null</c> when it can. The server
    /// compiles it as a .NET regular expression and fails with an unclear error when it isn't one; a common slip is a
    /// single backslash before a folder name (<c>\uploads</c> instead of <c>\\uploads</c>).
    /// </summary>
    public static string? GetPathError(string absolutePath)
    {
        if (absolutePath.Contains('"'))
        {
            return $"The skip rule path '{absolutePath}' contains a double quote, which Web Deploy can't receive.";
        }

        try
        {
            _ = new System.Text.RegularExpressions.Regex(absolutePath);
            return null;
        }
        catch (ArgumentException ex)
        {
            return $"The skip rule path '{absolutePath}' isn't a valid regular expression ({ex.Message}). A backslash in a path is written \\\\, e.g. \\\\uploads$.";
        }
    }
}
