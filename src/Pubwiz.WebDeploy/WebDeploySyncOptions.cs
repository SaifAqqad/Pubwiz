namespace Pubwiz.WebDeploy;

/// <summary>A progress message from the server during a sync (e.g. "Adding file (PubwizTest\index.html).").</summary>
/// <param name="Operation">The operation type, such as <c>AddChild</c>.</param>
/// <param name="ObjectName">The kind of object, such as <c>filePath</c> or <c>dirPath</c>.</param>
/// <param name="Path">The destination path the message is about.</param>
/// <param name="Message">The human-readable message.</param>
public sealed record WebDeployProgress(string Operation, string ObjectName, string Path, string Message);

/// <summary>Options for a folder sync.</summary>
public sealed class WebDeploySyncOptions
{
    /// <summary>Accept the server's TLS certificate even when it fails validation (WMSVC is self-signed by default).</summary>
    public bool AllowUntrusted { get; set; }

    /// <summary>Called for each progress message the server reports while applying the sync.</summary>
    public Action<WebDeployProgress>? OnProgress { get; set; }

    /// <summary>How long to wait for the whole operation before giving up. Defaults to 10 minutes.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);
}
