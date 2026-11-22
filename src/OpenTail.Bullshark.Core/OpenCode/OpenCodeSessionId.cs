using System.Text.RegularExpressions;

namespace OpenTail.Bullshark.Core.OpenCode;

/// <summary>
/// The shape of an opencode session id (OC-D8). The shape was verified at OC0: the program checks only the prefix <c>ses</c>; the rest is derived from
/// its id generator (<c>ses_</c>, then 12 lowercase hexadecimal characters, then 14 base62 characters). An id of any other shape is refused before it
/// goes into a command line.
/// </summary>
public static class OpenCodeSessionId
{
    /// <summary>The whole id. <c>\A</c> and <c>\z</c> are used, not <c>^</c> and <c>$</c>, because <c>$</c> also matches before a final newline.</summary>
    private static readonly Regex Shape = new(@"\Ases_[0-9a-f]{12}[0-9A-Za-z]{14}\z", RegexOptions.CultureInvariant);

    /// <summary>True when <paramref name="id"/> has the opencode session id shape. Null and empty are not.</summary>
    public static bool IsValid(string? id) => !string.IsNullOrEmpty(id) && Shape.IsMatch(id);
}
