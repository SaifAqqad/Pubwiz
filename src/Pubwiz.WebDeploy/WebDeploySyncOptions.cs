namespace Pubwiz.WebDeploy;

/// <summary>A progress message from the server during a sync (e.g. "Adding file (PubwizTest\index.html).").</summary>
/// <param name="Operation">The operation type, such as <c>AddChild</c>.</param>
/// <param name="ObjectName">The kind of object, such as <c>filePath</c> or <c>dirPath</c>.</param>
/// <param name="Path">The destination path the message is about.</param>
/// <param name="Message">The human-readable message.</param>
public sealed record WebDeployProgress(string Operation, string ObjectName, string Path, string Message);

/// <summary>Options for a folder sync. The defaults match msdeploy's, except that extra files are kept.</summary>
public sealed class WebDeploySyncOptions
{
    /// <summary>Accept the server's TLS certificate even when it fails validation (WMSVC is self-signed by default).</summary>
    public bool AllowUntrusted { get; set; }

    /// <summary>
    /// Put <c>app_offline.htm</c> in the application while syncing, so a running ASP.NET app stops and releases its
    /// files, and remove it afterwards (msdeploy's <c>-enableRule:AppOffline</c>).
    /// </summary>
    public bool TakeAppOffline { get; set; }

    /// <summary>
    /// Delete files and folders on the server that aren't in the source. Off by default: extra files are kept
    /// (msdeploy's <c>-enableRule:DoNotDeleteRule</c>). Use <see cref="SkipRules"/> to keep some of them while deleting the rest.
    /// </summary>
    public bool DeleteExtraFiles { get; set; }

    /// <summary>
    /// Only report what would change, without changing anything (msdeploy's <c>-whatif</c>). The result's actions are
    /// the changes a real sync would make.
    /// </summary>
    public bool WhatIf { get; set; }

    /// <summary>
    /// Compare files by content (checksum) instead of size and last-write time (msdeploy's <c>-useCheckSum</c>).
    /// Catches changes that keep a file's size and date, which a normal sync can miss.
    /// </summary>
    public bool UseChecksum { get; set; }

    /// <summary>Files and folders to leave alone (msdeploy's <c>-skip</c>).</summary>
    public IList<WebDeploySkipRule> SkipRules { get; } = [];

    /// <summary>How many times the server tries an operation that fails, e.g. on a locked file. Defaults to 5.</summary>
    public int RetryAttempts { get; set; } = 5;

    /// <summary>How long the server waits between those tries. Defaults to 1 second.</summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Called for each progress message the server reports while applying the sync.</summary>
    public Action<WebDeployProgress>? OnProgress { get; set; }

    /// <summary>How long to wait for the whole operation before giving up. Defaults to 10 minutes.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);
}
