namespace OpenTail.Bullshark.Core.Providers;

/// <summary>
/// The limits of a transcript, shared by every provider's reader and by the App's transcript view (parity P7, decision T-D5). One value, so the reader's
/// cap and the view's cap cannot drift apart. A session longer than this shows its first messages only, and the view says so.
/// </summary>
public static class TranscriptLimits
{
    /// <summary>The most messages one transcript shows.</summary>
    public const int MaxMessages = 5000;
}
