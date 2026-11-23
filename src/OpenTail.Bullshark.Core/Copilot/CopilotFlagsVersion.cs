using System.Text.RegularExpressions;

namespace OpenTail.Bullshark.Core.Copilot;

/// <summary>
/// The version rule for the Copilot copies that the setup check reads. Every Copilot command line the app builds passes these flags: <c>-C</c>,
/// <c>--sync-session=off</c>, <c>--config-dir</c>, <c>--available-tools</c> and <c>--no-custom-instructions</c>. The flag strings are in the 1.0.103 and
/// 1.0.104-3 builds, and in no older build on this PC. A copy older than <see cref="FirstVersionWithFlags"/> therefore fails the app's commands, so the
/// setup check shows it as a problem. The app does not choose between copies: the owner updates the older copy or removes it from PATH.
/// </summary>
public static class CopilotFlagsVersion
{
    /// <summary>The first build with the flags the app passes (see the class summary).</summary>
    public const string FirstVersionWithFlags = "1.0.103";

    /// <summary>Three numbers (major, minor, patch), then an optional suffix such as <c>-3</c>, which the comparison ignores.</summary>
    private static readonly Regex Shape = new(@"\A([0-9]+\.[0-9]+\.[0-9]+)(?:-[0-9A-Za-z.]+)?\z", RegexOptions.CultureInvariant);

    private static readonly Version Floor = new(FirstVersionWithFlags);

    /// <summary>
    /// True when <paramref name="version"/> is at least <see cref="FirstVersionWithFlags"/>, compared numerically on major, minor and patch. A suffix
    /// after a dash is ignored, so 1.0.104-3 is at least 1.0.103. Blank text, text that is not three numbers ("junk") and a number that does not fit
    /// give false.
    /// </summary>
    public static bool HasFlags(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return false;
        var match = Shape.Match(version);
        return match.Success && Version.TryParse(match.Groups[1].Value, out var parsed) && parsed >= Floor;
    }

    /// <summary>True when the text is a version: three numbers, then an optional dash suffix (1.0.34, 1.0.104-3).</summary>
    public static bool IsVersion(string text) => Numeric(text) is not null;

    /// <summary>
    /// Orders two version texts by major, minor and patch; a suffix is ignored. Text that is not a version sorts below any version (two non-versions are
    /// equal). Used to pick the newest downloaded bundle (PP-D2).
    /// </summary>
    public static int Compare(string left, string right)
    {
        var x = Numeric(left);
        var y = Numeric(right);
        if (x is null || y is null) return (x is null ? 0 : 1).CompareTo(y is null ? 0 : 1);
        return x.CompareTo(y);
    }

    private static Version? Numeric(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = Shape.Match(text);
        return match.Success && Version.TryParse(match.Groups[1].Value, out var parsed) ? parsed : null;
    }
}
