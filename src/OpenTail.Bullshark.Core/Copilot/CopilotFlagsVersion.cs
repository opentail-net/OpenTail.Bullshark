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
}
