using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Library;

/// <summary>
/// What the owner set on one past session (session library, D-L2). Every field is optional. An entry with nothing set is not kept.
/// </summary>
public sealed record LibraryEntry(
    string? Name = null,
    bool Pinned = false,
    bool Archived = false,
    IReadOnlyList<string>? Tags = null,
    string? Note = null,
    string? Colour = null)
{
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Name) && !Pinned && !Archived && (Tags is null || Tags.Count == 0)
        && string.IsNullOrWhiteSpace(Note) && string.IsNullOrWhiteSpace(Colour);
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

    public SessionLibrary(string path)
    {
        _path = path;
    }

    /// <summary>The library that sits beside a state file, so a run with its own state keeps its own marks.</summary>
    public static SessionLibrary Beside(string stateFile) =>
        new(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(stateFile))!, "library.json"));

    public string FilePath => _path;

    /// <summary>All entries by session id. A missing file is empty. An unreadable file throws <see cref="StoreUnreadableException"/>.</summary>
    public IReadOnlyDictionary<string, LibraryEntry> Load()
    {
        using var _ = FileLock.Hold(_path);
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

    /// <summary>The tags typed in one box: separated by spaces, tabs or commas, lower case, without repeats (D-L6).</summary>
    public static IReadOnlyList<string> TagsOf(string? text) =>
        (text ?? string.Empty).Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.Trim().ToLowerInvariant())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private Dictionary<string, LibraryEntry> Read()
    {
        if (!File.Exists(_path)) return new Dictionary<string, LibraryEntry>(StringComparer.Ordinal);
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, LibraryEntry>>(File.ReadAllText(_path), Options);
            return new Dictionary<string, LibraryEntry>(parsed ?? [], StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            throw new StoreUnreadableException(_path, ex.Message);
        }
    }

    private void Write(IReadOnlyDictionary<string, LibraryEntry> entries)
    {
        // Sorted by session id, so the file reads the same however the entries were added.
        var sorted = new SortedDictionary<string, LibraryEntry>(StringComparer.Ordinal);
        foreach (var pair in entries) sorted[pair.Key] = pair.Value;
        FileLock.WriteAtomic(_path, JsonSerializer.Serialize(sorted, Options));
    }
}
