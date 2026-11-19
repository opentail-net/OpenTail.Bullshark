using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OpenTail.Bullshark.Core.Ui;

/// <summary>
/// A signature of what the rail draws (perf and testability batch, Q5). The rail is built again only when its signature differs from the last one
/// built: a pass that finds the same records, runs, marks, listings, facts and settings keeps the rows, so the scroll and the selection stay where
/// the owner left them. The signature is the text of the named parts, each length-prefixed, so two different sets of parts never give the same text.
/// Equal text means equal parts; a part that is null is written apart from an empty one. Pure.
/// </summary>
public sealed class RailSignature
{
    private readonly StringBuilder _text = new();

    /// <summary>Adds one named text part. A null part is different from an empty one.</summary>
    public RailSignature Add(string name, string? value)
    {
        _text.Append(name).Append('=');
        if (value is null) _text.Append('-');
        else _text.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
        _text.Append(';');
        return this;
    }

    /// <summary>Adds one named part as its JSON text (records, lists and dictionaries of plain values). A null part is written as null.</summary>
    public RailSignature AddJson<T>(string name, T value) => Add(name, value is null ? null : JsonSerializer.Serialize(value));

    /// <summary>The signature so far.</summary>
    public string Build() => _text.ToString();

    /// <summary>
    /// The minute of a time in UTC, as text. The rail's clock-dependent text (ages such as "1h ago", the cache's state, a waiting run's start) is
    /// drawn again when this changes, so it is at most a minute old.
    /// </summary>
    public static string MinuteOf(DateTimeOffset now) => now.ToUniversalTime().ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
}
