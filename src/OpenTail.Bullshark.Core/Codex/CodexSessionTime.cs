namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// The start time that some Codex session ids carry. A newer rollout's id is a UUIDv7: its version nibble (the 15th hex digit) is 7, and its
/// first 48 bits are the Unix time in milliseconds. An older id is not a UUIDv7 and carries no time, so no time is assumed for it. This is a pure
/// check of the id's text: no file is read, and nothing is cached.
/// </summary>
internal static class CodexSessionTime
{
    /// <summary>The length of a UUID in its hyphenated form.</summary>
    private const int IdLength = 36;

    /// <summary>The index of the version nibble: the first digit of the third group.</summary>
    private const int VersionIndex = 14;

    /// <summary>
    /// True when <paramref name="id"/> is a UUIDv7 (36 characters, hyphens at the usual places, version nibble 7, hex digits elsewhere); the start
    /// is its first 48 bits read as Unix milliseconds, in UTC. Upper-case and lower-case hex are both accepted, as the file-name UUID test accepts both.
    /// The variant bits are not checked. False for any other text, and for a time past the last instant a <see cref="DateTimeOffset"/> holds.
    /// </summary>
    public static bool TryStartFromId(string id, out DateTimeOffset start)
    {
        start = default;
        if (id is null || id.Length != IdLength) return false;
        if (id[8] != '-' || id[13] != '-' || id[18] != '-' || id[23] != '-') return false;
        if (id[VersionIndex] != '7') return false;

        long millis = 0;
        for (var i = 0; i < IdLength; i++)
        {
            if (i is 8 or 13 or 18 or 23) continue;
            var digit = HexValue(id[i]);
            if (digit < 0) return false;

            // The first 12 hex digits (indices 0-7 and 9-12) are the 48-bit timestamp; the rest is random.
            if (i < 8 || (i > 8 && i < 13)) millis = millis * 16 + digit;
        }

        if (millis > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()) return false;
        start = DateTimeOffset.FromUnixTimeMilliseconds(millis);
        return true;
    }

    /// <summary>The value of one hex digit, or -1 when the character is not one.</summary>
    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };
}
