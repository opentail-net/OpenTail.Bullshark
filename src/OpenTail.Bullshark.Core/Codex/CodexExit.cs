using System.Globalization;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// How a Codex terminal tab's process ended (R6, concept note B). <see cref="Expected"/> is true when the owner closed the tab or the app shut
/// down, so the end is not a failure. <see cref="ExitCode"/> is the process's exit code, or null when none was reported. <see cref="Text"/> is
/// the line the tab shows.
/// </summary>
public sealed record ExitRecord(bool Expected, int? ExitCode, string Text);

/// <summary>
/// Classifies the end of a Codex terminal tab's process (R6). An exit the owner caused by closing the tab, or the app caused by shutting down,
/// is expected. Any other exit is not. Only the expected flag, the exit code and the text are made here: no tab title or session name is part of
/// the record, so none of them can reach the log through it.
/// </summary>
public static class CodexExit
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
}
