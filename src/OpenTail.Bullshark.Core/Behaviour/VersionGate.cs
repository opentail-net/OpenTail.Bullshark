using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenTail.Bullshark.Core.Behaviour;

/// <summary>What a version check says about an installed CLI (R4).</summary>
public enum VersionVerdict
{
    /// <summary>The installed version is the minimum or a later one.</summary>
    Supported,

    /// <summary>The installed version is below the minimum: the launch is refused.</summary>
    TooOld,

    /// <summary>
    /// The version could not be read or compared: a program that cannot be run, a timeout, a failed command, output with no version number, or a minimum
    /// that is not a dotted version. <c>versions.unreadable</c> decides whether the launch is refused or goes on with a warning. It is never "too old".
    /// </summary>
    CouldNotDetermine,
}

/// <summary>
/// The result of a version check. <see cref="Installed"/> is the version number read from the output (null when none was read), and <see cref="Minimum"/>
/// is the minimum it was compared with. <see cref="Reason"/> says why the verdict is <see cref="VersionVerdict.CouldNotDetermine"/>. It never holds the
/// output text, which can contain a machine path.
/// </summary>
public sealed record VersionGateResult(VersionVerdict Verdict, string? Installed, string Minimum, string? Reason = null);

/// <summary>
/// Compares an installed CLI's version with its minimum (R4). Pure: it reads no file and runs no program. A version is a dotted number such as "2.1.330".
/// The numeric segments are compared as numbers, so "2.1.4" is below "2.1.330", and "2.1" equals "2.1.0". A "v" prefix, and a suffix or pre-release tag after
/// a hyphen or a plus sign, is ignored. A value that does not parse is <see cref="VersionVerdict.CouldNotDetermine"/>, never too old.
/// </summary>
public static class VersionGate
{
    /// <summary>A version number in a tool's output: dot-separated digits of at least two parts, with an optional "v" (for example "2.1.330" in "2.1.330 (Claude Code)").</summary>
    private static readonly Regex VersionToken = new(@"(?<![0-9.])v?([0-9]+(?:\.[0-9]+)+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The first version number in a tool's output, as written ("2.1.330"), or null when the text has none.</summary>
    public static string? VersionIn(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var match = VersionToken.Match(output);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Orders two versions by their numeric segments: -1, 0 or 1. Null when either one does not parse.
    /// </summary>
    public static int? Compare(string? installed, string? minimum)
    {
        var left = SegmentsOf(installed);
        var right = SegmentsOf(minimum);
        if (left is null || right is null) return null;

        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var a = i < left.Length ? left[i] : 0;
            var b = i < right.Length ? right[i] : 0;
            if (a != b) return a < b ? -1 : 1;
        }
        return 0;
    }

    /// <summary>
    /// The verdict for the output of a version command, against <paramref name="minimum"/>. The result carries the version number it read and the minimum
    /// as it was given. No output text is kept in it.
    /// </summary>
    public static VersionGateResult Check(string? output, string minimum)
    {
        var installed = VersionIn(output);
        if (installed is null)
            return new VersionGateResult(VersionVerdict.CouldNotDetermine, null, minimum, "no version number in the output");
        if (SegmentsOf(minimum) is null)
            return new VersionGateResult(VersionVerdict.CouldNotDetermine, installed, minimum, "the minimum is not a dotted version");

        return Compare(installed, minimum) switch
        {
            null => new VersionGateResult(VersionVerdict.CouldNotDetermine, installed, minimum, "the version could not be compared"),
            < 0 => new VersionGateResult(VersionVerdict.TooOld, installed, minimum),
            _ => new VersionGateResult(VersionVerdict.Supported, installed, minimum),
        };
    }

    /// <summary>
    /// The numeric segments of a dotted version ("2.1.330" gives 2, 1, 330), or null when the text is not one: blank, or a part that is empty, not all
    /// digits, or too large for an int. A "v" prefix and a suffix after a hyphen or a plus sign are dropped first.
    /// </summary>
    private static int[]? SegmentsOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var core = text.Trim();
        if (core[0] is 'v' or 'V') core = core[1..];
        var cut = core.IndexOfAny(['-', '+']);
        if (cut >= 0) core = core[..cut];

        var parts = core.Split('.');
        var segments = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0 || !part.All(char.IsAsciiDigit)) return null;
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out segments[i])) return null;
        }
        return segments;
    }
}
