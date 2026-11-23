namespace OpenTail.Bullshark.Core.Providers;

/// <summary>
/// The text of every message of one session, in order, with no role and no cap (PP-D4, 2026-11-23). Only the message-text search reads it, so the
/// transcript's own page reader is not changed. Claude's history is not a source here: its search already reads its messages.
/// </summary>
public interface IMessageTextSource
{
    /// <summary>
    /// The text of each message of the session, in file order. An unsafe id throws <see cref="ArgumentException"/>; a session whose file is missing
    /// throws <see cref="FileNotFoundException"/>; a read that cannot be done throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    Task<IReadOnlyList<string>> MessageTextsAsync(string sessionId, CancellationToken ct);
}
