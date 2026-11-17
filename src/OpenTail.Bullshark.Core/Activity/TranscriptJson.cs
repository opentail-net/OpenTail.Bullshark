using System.Globalization;
using System.Text.Json;

namespace OpenTail.Bullshark.Core.Activity;

/// <summary>
/// The line helpers every transcript reader shares (round 12 item A1). A line is parsed here and its values are read with these accessors.
/// A value that cannot be decoded (a lone surrogate escape such as <c>\ud800</c>, which <see cref="JsonElement.GetString"/> refuses) throws
/// <see cref="InvalidOperationException"/>, as does a property read on a value that is not an object. A reader reads a line's values
/// before it counts anything, inside a catch of <see cref="IsUnreadable"/>, and skips the line when one of them throws: a line that cannot
/// be read costs that line only, as a line that is not JSON already does.
/// </summary>
internal static class TranscriptJson
{
    /// <summary>
    /// Parses one line. False for a blank line and for a line that is not JSON. A byte order mark at the start is ignored: the pager reads
    /// the first line of a file without detecting the mark, so it stays in that line's text.
    /// </summary>
    public static bool TryParse(string line, out JsonDocument document)
    {
        var json = line.Length > 0 && line[0] == '﻿' ? line[1..] : line;
        if (string.IsNullOrWhiteSpace(json))
        {
            document = null!;
            return false;
        }
        try
        {
            document = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            document = null!;
            return false;
        }
    }

    /// <summary>True for the exceptions a line's values raise when they cannot be read. A reader skips that line.</summary>
    public static bool IsUnreadable(Exception ex) => ex is InvalidOperationException or JsonException;

    /// <summary>The string a property holds, or null when the element is not an object, the property is missing, or it is not a string.</summary>
    public static string? Str(JsonElement element, string property) =>
        TryStr(element, property, out var value) ? value : null;

    /// <summary>The string a property holds. False when the element is not an object, the property is missing, or it is not a string.</summary>
    public static bool TryStr(JsonElement element, string property, out string? value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String)
        {
            value = v.GetString();
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>The integer a property holds, or null when the element is not an object, the property is missing, or it is not an integer.</summary>
    public static long? Number(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var v) &&
        v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)
            ? n
            : null;

    /// <summary>As <see cref="Number"/>, with 0 for a missing or non-integer value.</summary>
    public static long Long(JsonElement element, string property) => Number(element, property) ?? 0;

    /// <summary>
    /// A timestamp property, read as the transcripts write it. A time with no zone is taken as UTC. Null when the property is missing or does
    /// not parse.
    /// </summary>
    public static DateTimeOffset? Time(JsonElement element, string property) =>
        Str(element, property) is { } raw &&
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}
