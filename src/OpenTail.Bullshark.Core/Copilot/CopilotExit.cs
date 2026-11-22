using System.Globalization;
using OpenTail.Bullshark.Core.Codex;

namespace OpenTail.Bullshark.Core.Copilot;

/// <summary>
/// Classifies the end of a Copilot terminal tab's process (G2.8, G-D20), the same rule as Codex's (<see cref="CodexExit"/>). An exit the owner caused
/// by closing the tab, or the app caused by shutting down, is expected. Any other exit is not. The record is the shared <see cref="ExitRecord"/>; only
/// the expected flag, the exit code and the text are made here, so no tab title or session name can reach the log through it.
/// </summary>
public static class CopilotExit
{
    /// <summary>
    /// The record for one exit. Expected when <paramref name="closedByOwner"/> or <paramref name="appShuttingDown"/> is true, and the text is
    /// "Ended." then. Otherwise the text is "Ended with exit code N." when <paramref name="exitCode"/> is known, or "Ended; no exit code was
    /// reported." when it is not.
    /// </summary>
    public static ExitRecord Classify(bool closedByOwner, bool appShuttingDown, int? exitCode)
    {
        var expected = closedByOwner || appShuttingDown;
        var text = expected
            ? "Ended."
            : exitCode is { } code
                ? $"Ended with exit code {code.ToString(CultureInfo.InvariantCulture)}."
                : "Ended; no exit code was reported.";
        return new ExitRecord(expected, exitCode, text);
    }

    /// <summary>The suffix an ended Copilot tab's title gets (G2.8).</summary>
    public const string EndedSuffix = " (ended)";

    /// <summary>
    /// The title of an ended Copilot tab: <paramref name="title"/> with <see cref="EndedSuffix"/> added. A title that already ends with the suffix is
    /// returned as it is, so applying this again (on each title refresh) never doubles it.
    /// </summary>
    public static string EndedTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return title.EndsWith(EndedSuffix, StringComparison.Ordinal) ? title : title + EndedSuffix;
    }
}
