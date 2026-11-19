using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenTail.Bullshark.Core.Health;

/// <summary>
/// The sleep timeout on AC power, read from `powercfg /query SCHEME_CURRENT SUB_SLEEP STANDBYIDLE` (night batch N3). The command is read-only and
/// is run with a timeout by the window; this class only reads its text. Nothing here changes a power setting: keep-awake is the owner's call.
/// </summary>
public static class PowerSettings
{
    /// <summary>The arguments of the read-only query, in the order `powercfg` takes them.</summary>
    public static readonly IReadOnlyList<string> QueryArguments = ["/query", "SCHEME_CURRENT", "SUB_SLEEP", "STANDBYIDLE"];

    /// <summary>
    /// The program that is run: powercfg in the system folder. A bare name would be searched in the current folder first, so a program of that name
    /// there would run instead (review R12).
    /// </summary>
    public static string ProgramPath => Path.Combine(Environment.SystemDirectory, "powercfg.exe");

    private static readonly Regex AcIndex = new(@"Current AC Power Setting Index:\s*0x([0-9a-fA-F]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The AC sleep timeout in whole minutes, rounded up: the value is in seconds (hex), and 0 means "never" (returned as 0). A value of 30 s
    /// is 1 minute. Null when the output has no such line (another language, or a command that failed). Never throws.
    /// </summary>
    public static int? ParseSleepMinutes(string? output)
    {
        if (string.IsNullOrEmpty(output)) return null;
        var match = AcIndex.Match(output);
        if (!match.Success) return null;
        if (!long.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var seconds)) return null;
        if (seconds <= 0) return 0;
        var minutes = (seconds + 59) / 60;
        return minutes > int.MaxValue ? int.MaxValue : (int)minutes;
    }
}
