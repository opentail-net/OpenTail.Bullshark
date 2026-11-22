namespace OpenTail.Bullshark.App;

/// <summary>
/// The rule a resume screen uses when a summary that was asked for is not made (owner, 2026-11-22). Pure, so the tests need no window.
/// </summary>
public static class ResumeDefaults
{
    /// <summary>The line added after the problem in the preview area when a failed summary falls back to In full.</summary>
    public const string FullFallbackLine = "In full is selected instead.";

    /// <summary>
    /// True when a summary that was not made falls back to In full: the mode was a summary, and the request was not cancelled (a cancelled request keeps
    /// the selection).
    /// </summary>
    public static bool FallBackToFull(bool wasSummary, bool cancelled) => wasSummary && !cancelled;
}
