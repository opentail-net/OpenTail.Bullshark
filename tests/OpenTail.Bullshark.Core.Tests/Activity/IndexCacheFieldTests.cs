using System.Globalization;
using System.Reflection;
using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// The index cache keeps a transcript's summary as a DTO and restores it from there (round 11 item 5). A property added to
/// <see cref="TranscriptSummary"/> or <see cref="SessionDigest"/> that the DTO does not carry is lost for every cached session, and nothing
/// says so. These tests fail when that happens: they list the public properties by reflection and compare them with explicit lists, and they
/// round-trip a summary with every carried field set. IndexCache is internal and Core has no InternalsVisibleTo, so it is reached by reflection.
/// </summary>
public sealed class IndexCacheFieldTests
{
    /// <summary>The TranscriptSummary properties the cache carries (SummaryDto, EntryOf and Restore). The round-trip test checks each one.</summary>
    private static readonly string[] CarriedSummaryProperties =
    [
        "Records", "UserMessages", "AssistantMessages", "ToolCalls", "CompletedTurns", "LastAssistantText", "LastTimestamp", "LastModel",
        "FirstTimestamp", "ToolNames", "FilesWritten", "ContextTokensAtLastTurn", "FirstUserText", "AiTitle", "CustomTitle", "LastRecap",
        "LastRecapAt", "Compactions", "ContinuesSessionId", "FailedToolCalls", "LatestCwd",
    ];

    /// <summary>The TranscriptSummary properties the cache does not carry. Each one has its reason; nothing else may be left out.</summary>
    private static readonly string[] AllowedSummaryProperties =
    [
        "UsageByModel",   // rebuilt from the ledger by the index: Restore leaves it empty and the index sets it from the restored ledger.
        "TotalUsage",     // computed from UsageByModel.
        "FileLastWrites", // not read by DigestOf, so a cached session gives the same digest without it.
        "FileFolders",    // not read by DigestOf.
    ];

    /// <summary>The SessionDigest properties that DigestOf takes from the transcript's file path.</summary>
    private static readonly string[] FromFilePathDigestProperties = ["SessionId", "FilePath", "ProjectFolder", "IsSubagent"];

    /// <summary>
    /// The SessionDigest properties that DigestOf takes from the summary. Each one comes from a summary property the cache carries, or (for
    /// Usage) from UsageByModel, which the cache rebuilds from the messages it keeps.
    /// </summary>
    private static readonly string[] FromSummaryDigestProperties =
    [
        "FirstActivity", "LastActivity", "Turns", "UserMessages", "AssistantMessages", "ToolCalls", "ToolNames", "FilesWritten",
        "LastAssistantText", "LastModel", "ContextTokensAtLastTurn", "Usage", "FirstUserText", "AiTitle", "CustomTitle", "LastRecap",
        "LastRecapAt", "Compactions", "ContinuesSessionId", "FailedToolCalls", "Cwd",
    ];

    /// <summary>SessionDigest properties computed from the others. Span is computed from FirstActivity and LastActivity.</summary>
    private static readonly string[] ComputedDigestProperties = ["Title", "Span"];

    [Fact]
    public void EveryTranscriptSummaryPropertyIsCarriedByTheCacheOrAllowedWithAReason()
    {
        AssertListed(
            typeof(TranscriptSummary),
            CarriedSummaryProperties.Concat(AllowedSummaryProperties),
            "Add it to SummaryDto, EntryOf and Restore in IndexCache.cs and to CarriedSummaryProperties in this file, or, if the digest " +
            "does not need it, to AllowedSummaryProperties in this file with a reason.");
    }

    [Fact]
    public void EverySessionDigestPropertyIsSetFromThePathOrTheSummaryOrComputed()
    {
        AssertListed(
            typeof(SessionDigest),
            FromFilePathDigestProperties.Concat(FromSummaryDigestProperties).Concat(ComputedDigestProperties),
            "Add it to FromFilePathDigestProperties, FromSummaryDigestProperties or ComputedDigestProperties in this file. A property set " +
            "from the summary also needs the summary property it comes from to be carried by the cache (SummaryDto, EntryOf, Restore, and " +
            "CarriedSummaryProperties here).");
    }

    [Fact]
    public void ASummaryWithEveryCarriedFieldSetRoundTripsThroughTheCache()
    {
        // Timestamps carry a non-zero offset, the dictionary and the list have two items, and every number is distinct.
        var offset = TimeSpan.FromHours(5.5);
        var original = new TranscriptSummary(
            Records: 41,
            UserMessages: 7,
            AssistantMessages: 9,
            ToolCalls: 13,
            CompletedTurns: 4,
            LastAssistantText: "last answer",
            LastTimestamp: new DateTimeOffset(2026, 11, 2, 9, 30, 15, offset),
            LastModel: "claude-opus-5-5",
            UsageByModel: new Dictionary<string, TokenUsage> { ["claude-opus-5-5"] = new TokenUsage(100, 200, 300, 400) },
            FirstTimestamp: new DateTimeOffset(2026, 11, 1, 8, 5, 0, offset),
            ToolNames: new Dictionary<string, int> { ["Read"] = 5, ["Bash"] = 8 },
            FilesWritten: new List<string> { "notes/a.md", "src/b.cs" },
            ContextTokensAtLastTurn: 123456,
            FirstUserText: "first prompt",
            AiTitle: "AI title",
            CustomTitle: "Custom title",
            LastRecap: "recap text",
            LastRecapAt: new DateTimeOffset(2026, 11, 2, 9, 31, 0, offset),
            Compactions: 2,
            ContinuesSessionId: "11111111-2222-3333-4444-555555555555",
            FailedToolCalls: 3,
            LatestCwd: @"C:\Git\split\OpenTail.Bullshark");

        var cache = typeof(TranscriptIndex).Assembly.GetType("OpenTail.Bullshark.Core.Activity.IndexCache", throwOnError: true)!;
        var entryOf = cache.GetMethod("EntryOf", BindingFlags.Static | BindingFlags.Public)!;
        var restore = cache.GetMethod("Restore", BindingFlags.Static | BindingFlags.Public)!;
        var entryType = cache.GetNestedType("Entry", BindingFlags.Public)!;

        var entry = entryOf.Invoke(null, new object[]
        {
            "session.jsonl",
            4096L,
            new DateTime(2026, 11, 2, 4, 0, 0, DateTimeKind.Utc),
            original,
            Enumerable.Empty<KeyValuePair<string, UsageLedger.Entry>>(),
        })!;

        // The entry goes through JSON as the cache file does, so a JSON name that does not survive shows here.
        var json = JsonSerializer.Serialize(entry, entryType);
        var read = JsonSerializer.Deserialize(json, entryType)!;

        var restored = restore.Invoke(null, new object[] { read })!;
        var summary = (TranscriptSummary)restored.GetType().GetField("Item1")!.GetValue(restored)!;

        foreach (var name in CarriedSummaryProperties)
        {
            var property = typeof(TranscriptSummary).GetProperty(name)!;
            var before = property.GetValue(original);
            var after = property.GetValue(summary);
            Assert.False(IsDefault(before), $"{name} is at its default in the fixture, so the round trip cannot show that it is kept. Set it in this test.");
            Assert.True(Content(before) == Content(after),
                $"{name} was {Content(before)} and is {Content(after)} after the cache round trip. Check SummaryDto, EntryOf and Restore in IndexCache.cs.");
        }
    }

    /// <summary>
    /// Checks that the listed names are exactly the public instance properties of <paramref name="type"/>, and names each property that is
    /// in neither list (and each listed name that is not a property).
    /// </summary>
    private static void AssertListed(Type type, IEnumerable<string> listed, string advice)
    {
        var actual = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name).ToArray();
        var expected = listed.ToArray();

        var unlisted = actual.Except(expected).OrderBy(name => name, StringComparer.Ordinal).ToList();
        Assert.True(unlisted.Count == 0, $"{type.Name} has the property {string.Join(", ", unlisted)}, which is in neither list. {advice}");

        var missing = expected.Except(actual).OrderBy(name => name, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, $"{type.Name} has no public property {string.Join(", ", missing)}. Remove it from the list in this file.");
    }

    /// <summary>A property value as text. Dictionaries are compared by key, lists by order, and dates with their offset.</summary>
    private static string Content(object? value) => value switch
    {
        null => "null",
        string text => "\"" + text + "\"",
        DateTimeOffset time => time.ToString("O", CultureInfo.InvariantCulture),
        IReadOnlyDictionary<string, int> counts =>
            "{" + string.Join(", ", counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value)) + "}",
        IEnumerable<string> items => "[" + string.Join(", ", items.Select(item => "\"" + item + "\"")) + "]",
        IConvertible number => number.ToString(CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException($"Add {value.GetType().Name} to Content and IsDefault in IndexCacheFieldTests."),
    };

    /// <summary>True for a value the fixture did not set: null, zero, an empty text or list, or the default time.</summary>
    private static bool IsDefault(object? value) => value switch
    {
        null => true,
        string text => text.Length == 0,
        DateTimeOffset time => time == default,
        IReadOnlyDictionary<string, int> counts => counts.Count == 0,
        IEnumerable<string> items => !items.Any(),
        int number => number == 0,
        long number => number == 0,
        _ => throw new NotSupportedException($"Add {value.GetType().Name} to Content and IsDefault in IndexCacheFieldTests."),
    };
}
