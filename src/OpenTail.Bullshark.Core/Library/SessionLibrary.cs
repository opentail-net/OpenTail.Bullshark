using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Library;

/// <summary>
/// What the owner set on one past session (session library, D-L2). Every field is optional. An entry with nothing set is not kept.
/// <see cref="MagicAt"/> and <see cref="MagicNamedAt"/> are set only when <see cref="Name"/> was given by the magic rename (D2, D6):
/// <see cref="MagicAt"/> is the session's last message when it was named (the snowflake rule), and <see cref="MagicNamedAt"/> is when it
/// was named (the date shown, and the automatic limit per hour). A file written before them loads with both null.
/// <see cref="PlanFile"/> is the absolute path of a Markdown plan file the owner attached by hand; its checkboxes show as progress on the row
/// (plan progress, 2026-11-18). A file written before it loads with null.
/// <see cref="AutoWake"/> is the owner's choice "Wake after the limit resets" for this session (limit wake, 2026-11-20). <see cref="AutoWokenHitAt"/> is
/// the limit hit (its time) that was last woken, so the same hit is not woken twice, and <see cref="AutoWakeTimes"/> are the wakes of the last day,
/// for the cap of three a day. Files written before them load with the defaults.
/// </summary>
public sealed record LibraryEntry(
    string? Name = null,
    bool Pinned = false,
    bool Archived = false,
    IReadOnlyList<string>? Tags = null,
    string? Note = null,
    string? Colour = null,
    DateTimeOffset? MagicAt = null,
    DateTimeOffset? MagicNamedAt = null,
    string? PlanFile = null,
    bool AutoWake = false,
    DateTimeOffset? AutoWokenHitAt = null,
    IReadOnlyList<DateTimeOffset>? AutoWakeTimes = null)
{
    /// <summary>
    /// True when nothing is set. The wake fields count as set: an entry with only the flag, or with the record of a wake, is kept (the record stops
    /// a second wake of the same hit, and the cap of the day).
    /// </summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Name) && !Pinned && !Archived && (Tags is null || Tags.Count == 0)
        && string.IsNullOrWhiteSpace(Note) && string.IsNullOrWhiteSpace(Colour) && string.IsNullOrWhiteSpace(PlanFile)
        && !AutoWake && AutoWokenHitAt is null && (AutoWakeTimes is null || AutoWakeTimes.Count == 0);
}

/// <summary>
/// The owner's marks on past sessions, kept in <c>library.json</c> beside the state file, keyed by session id (D-L2). It follows
/// <see cref="RunStore"/>: every change is read again under the file lock and written atomically. A file that cannot be read throws
/// <see cref="StoreUnreadableException"/> and is not overwritten. The rail shows without the marks in that case (D-L3).
/// </summary>
public sealed class SessionLibrary
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Action<string> _log;
    private bool _nullEntryLogged;

    public SessionLibrary(string path, Action<string>? log = null)
    {
        _path = path;
        _log = log ?? (_ => { });
    }

    /// <summary>The library that sits beside a state file, so a run with its own state keeps its own marks.</summary>
    public static SessionLibrary Beside(string stateFile, Action<string>? log = null) =>
        new(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(stateFile))!, "library.json"), log);

    public string FilePath => _path;

    /// <summary>All entries by session id. A missing file is empty. An unreadable file throws <see cref="StoreUnreadableException"/>.</summary>
    public IReadOnlyDictionary<string, LibraryEntry> Load() => Load(FileLock.DefaultWait);

    /// <summary>The same read, waiting at most <paramref name="wait"/> for the lock (reliability batch R7). A lock not free in time throws <see cref="TimeoutException"/>.</summary>
    public IReadOnlyDictionary<string, LibraryEntry> Load(TimeSpan wait)
    {
        using var _ = FileLock.Hold(_path, wait);
        return Read();
    }

    /// <summary>
    /// Applies a change under the file lock. The file is read again inside the lock first, so a change another process made
    /// is kept. Entries left with nothing set are removed before the write.
    /// </summary>
    public void Update(Action<Dictionary<string, LibraryEntry>> change)
    {
        using var _ = FileLock.Hold(_path);
        var entries = Read();
        change(entries);
        foreach (var key in entries.Where(pair => pair.Value.IsEmpty).Select(pair => pair.Key).ToList()) entries.Remove(key);
        Write(entries);
    }

    /// <summary>The owner's name when one is set, else the title the transcript gives (D-L1, in full).</summary>
    public static string TitleFor(SessionDigest digest, LibraryEntry? entry) =>
        entry?.Name is { } name && !string.IsNullOrWhiteSpace(name) ? name.Trim() : digest.Title;

    /// <summary>
    /// The title with the lineage rule (L5). The owner's name first, then Claude's title. A resumed session with neither, and with
    /// no first message of its own, is "↪ " and the title of the session it continues (<paramref name="continued"/>, when it is known).
    /// </summary>
    public static string TitleFor(SessionDigest digest, LibraryEntry? entry, SessionDigest? continued)
    {
        if (entry?.Name is { } name && !string.IsNullOrWhiteSpace(name)) return name.Trim();
        if (digest.AiTitle is { Length: > 0 } aiTitle) return aiTitle;
        if (digest.ContinuesSessionId is { Length: > 0 } earlier && string.IsNullOrEmpty(digest.FirstUserText))
        {
            var earlierTitle = continued is not null
                ? TitleFor(continued, null)
                : $"Session {earlier[..Math.Min(8, earlier.Length)]}";
            return "↪ " + earlierTitle;
        }
        return digest.Title;
    }

    /// <summary>The tags typed in one box: separated by spaces, tabs or commas, lower case, without repeats (D-L6).</summary>
    public static IReadOnlyList<string> TagsOf(string? text) =>
        (text ?? string.Empty).Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.Trim().ToLowerInvariant())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The entries as the file holds them. A missing file is empty. The file is opened directly, not checked with File.Exists first:
    /// a file that exists but cannot be read (denied, or held by another process) is an error, not an empty library that the next
    /// save would overwrite. An entry that is null is no entry: it is left out, and logged once, rather than failing every later change.
    /// </summary>
    private Dictionary<string, LibraryEntry> Read()
    {
        string text;
        try
        {
            text = File.ReadAllText(_path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new Dictionary<string, LibraryEntry>(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new StoreUnreadableException(_path, ex.Message);
        }

        Dictionary<string, LibraryEntry>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, LibraryEntry>>(text, Options);
        }
        catch (JsonException ex)
        {
            throw new StoreUnreadableException(_path, ex.Message);
        }

        var entries = new Dictionary<string, LibraryEntry>(StringComparer.Ordinal);
        foreach (var pair in parsed ?? new Dictionary<string, LibraryEntry>(StringComparer.Ordinal))
        {
            if (pair.Value is not null)
            {
                entries[pair.Key] = pair.Value;
                continue;
            }
            if (_nullEntryLogged) continue;
            _nullEntryLogged = true;
            _log($"library: an entry with no value is left out ({_path}); the next save drops it.");
        }
        return entries;
    }

    private void Write(IReadOnlyDictionary<string, LibraryEntry> entries)
    {
        // Sorted by session id, so the file reads the same however the entries were added.
        var sorted = new SortedDictionary<string, LibraryEntry>(StringComparer.Ordinal);
        foreach (var pair in entries) sorted[pair.Key] = pair.Value;
        FileLock.WriteAtomic(_path, JsonSerializer.Serialize(sorted, Options));
    }
}
