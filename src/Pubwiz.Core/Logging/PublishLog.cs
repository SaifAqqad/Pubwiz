namespace Pubwiz.Core.Logging;

/// <summary>Where a log line came from.</summary>
public enum LogSource
{
    /// <summary>A message written by Pubwiz itself.</summary>
    Pubwiz,

    /// <summary>Standard output of an external tool.</summary>
    Output,

    /// <summary>Standard error of an external tool.</summary>
    Error
}

/// <summary>One line of a publish log.</summary>
/// <param name="Timestamp">When the line was written.</param>
/// <param name="Source">Who wrote it.</param>
/// <param name="Step">The step that was running, or <c>null</c> outside of steps.</param>
/// <param name="Text">The line, with secrets already masked.</param>
public sealed record LogEntry(DateTimeOffset Timestamp, LogSource Source, string? Step, string Text);

/// <summary>
/// The complete, in-memory log of one publish job. Nothing is written to disk until the user exports it.
/// </summary>
/// <remarks>
/// Lines are written by the job's thread and read by the UI thread, so all access is locked.
/// Registered secrets are replaced with <see cref="Mask"/> before a line is stored, so they can't leak into exports.
/// </remarks>
public sealed class PublishLog
{
    public const string Mask = "********";

    private readonly Lock _lock = new();
    private readonly List<LogEntry> _entries = [];
    private readonly HashSet<string> _secrets = [];

    /// <summary>The step that new lines are tagged with.</summary>
    public string? CurrentStep { get; set; }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>
    /// Masks <paramref name="secret"/> in every line written from now on.
    /// </summary>
    public void AddSecret(string? secret)
    {
        // Very short values would mask unrelated text, and are never real secrets.
        if (string.IsNullOrEmpty(secret) || secret.Length < 4)
        {
            return;
        }

        lock (_lock)
        {
            _secrets.Add(secret);
        }
    }

    public void Write(LogSource source, string text)
    {
        lock (_lock)
        {
            var masked = _secrets.Aggregate(text, (current, secret) => current.Replace(secret, Mask, StringComparison.Ordinal));
            _entries.Add(new LogEntry(DateTimeOffset.Now, source, CurrentStep, masked));
        }
    }

    public void Info(string message) => Write(LogSource.Pubwiz, message);

    /// <summary>The last <paramref name="count"/> lines.</summary>
    public IReadOnlyList<LogEntry> Tail(int count)
    {
        lock (_lock)
        {
            return _entries.Skip(Math.Max(0, _entries.Count - count)).ToList();
        }
    }

    /// <summary>A copy of all lines.</summary>
    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_lock)
        {
            return _entries.ToList();
        }
    }
}
