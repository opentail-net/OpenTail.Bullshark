using System.Text.RegularExpressions;

namespace OpenTail.Bullshark.Core.Health;

/// <summary>
/// Reads the login warning that sessions show in their terminal:
/// <c>⚠ Your login expires in 3 days · run /login to renew</c> (finding F8).
/// The supervisor reads it from `claude logs` and raises an alert before the login lapses.
/// </summary>
public static class LoginExpiry
{
    private static readonly Regex EscapeCodes =
        new(@"\x1B\[[0-9;?]*[ -/]*[@-~]|\x1B\][^\x07]*\x07|\x1B[@-Z\\-_]", RegexOptions.Compiled);

    private static readonly Regex Warning =
        new(@"login expires in (\d+) (day|days|hour|hours)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Returns the time left before the login expires, or null if no warning is shown.</summary>
    public static TimeSpan? Find(string terminalText)
    {
        var plain = EscapeCodes.Replace(terminalText, string.Empty);
        var matches = Warning.Matches(plain);
        if (matches.Count == 0) return null;

        // The most recent warning is the one on screen.
        var last = matches[^1];
        var amount = int.Parse(last.Groups[1].Value);
        return last.Groups[2].Value.StartsWith("hour", StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromHours(amount)
            : TimeSpan.FromDays(amount);
    }

    /// <summary>The owner's threshold: alert when the login has two days or less left (P7).</summary>
    public static bool IsExpiringSoon(TimeSpan remaining) => remaining <= TimeSpan.FromDays(2);
}
