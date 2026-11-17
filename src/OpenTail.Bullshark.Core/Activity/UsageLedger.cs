namespace OpenTail.Bullshark.Core.Activity;

/// <summary>
/// Token usage per API message, counted once across all transcript files (F34, F35, F36).
/// One response is written as several records, and the same message can appear in more than one file. Each message is
/// keyed by <c>message.id:requestId</c>. The first record of a message can hold a placeholder output count, so the
/// largest value per field is kept (F36), not the first. Messages with no key are counted once per record.
/// </summary>
public sealed class UsageLedger
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>One counted message: its model, the first file it was seen in, and its largest usage.</summary>
    public sealed record Entry(string Model, string FirstFile, TokenUsage Usage);

    public int Count => _entries.Count;

    public void Record(string key, string model, string file, TokenUsage usage)
    {
        _entries[key] = _entries.TryGetValue(key, out var existing)
            ? existing with { Usage = Max(existing.Usage, usage) }
            : new Entry(model, file, usage);
    }

    /// <summary>All messages, across all files.</summary>
    public TokenUsage Total => _entries.Values.Aggregate(TokenUsage.Zero, (sum, e) => sum.Add(e.Usage));

    /// <summary>Totals per model, across all files.</summary>
    public IReadOnlyDictionary<string, TokenUsage> ByModel() => Group(_entries.Values);

    /// <summary>Totals per model for the messages first seen in one file.</summary>
    public IReadOnlyDictionary<string, TokenUsage> ByFile(string file) =>
        Group(_entries.Values.Where(e => e.FirstFile == file));

    /// <summary>Whether a message with this key has been counted.</summary>
    internal bool Contains(string key) => _entries.ContainsKey(key);

    /// <summary>The counted messages, in the order they were first counted.</summary>
    internal IEnumerable<KeyValuePair<string, Entry>> Pairs => _entries;

    /// <summary>Totals per model for the given messages: the grouping <see cref="ByFile"/> applies.</summary>
    internal static IReadOnlyDictionary<string, TokenUsage> ByModelOf(IEnumerable<Entry> entries) => Group(entries);

    private static IReadOnlyDictionary<string, TokenUsage> Group(IEnumerable<Entry> entries)
    {
        var byModel = new Dictionary<string, TokenUsage>(StringComparer.Ordinal);
        foreach (var e in entries)
            byModel[e.Model] = byModel.TryGetValue(e.Model, out var sum) ? sum.Add(e.Usage) : e.Usage;
        return byModel;
    }

    private static TokenUsage Max(TokenUsage a, TokenUsage b) => new(
        Math.Max(a.Input, b.Input), Math.Max(a.Output, b.Output),
        Math.Max(a.CacheCreation, b.CacheCreation), Math.Max(a.CacheRead, b.CacheRead));
}
