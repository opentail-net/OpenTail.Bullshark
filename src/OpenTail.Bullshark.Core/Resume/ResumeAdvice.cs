namespace OpenTail.Bullshark.Core.Resume;

/// <summary>
/// Which way to continue a past session is recommended from the size of its context (owner, 2026-11-16). A full resume re-reads the
/// whole context before the first reply, so it is recommended only while that context is small. Above the limit a summary is
/// recommended. The owner can still choose either, and the choice is shown with its cost before anything runs.
/// </summary>
public static class ResumeAdvice
{
    /// <summary>
    /// The context size, in tokens, up to which a full resume is recommended. It is about the 25th percentile of the sessions on
    /// this machine (48,000 tokens, measured 2026-11-16), so the smaller quarter of sessions resume in full.
    /// </summary>
    public const long FullResumeLimitTokens = 50_000;

    /// <summary>True when a full resume is the recommended way to continue a session with this much context.</summary>
    public static bool FullIsRecommended(long contextTokens) => contextTokens < FullResumeLimitTokens;
}
