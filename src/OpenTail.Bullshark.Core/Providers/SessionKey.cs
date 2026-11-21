namespace OpenTail.Bullshark.Core.Providers;

/// <summary>
/// A session's identity across providers (R-3). Two providers may use the same session id, so every dictionary, file and tag keyed
/// by a session uses this key. The text form is <c>provider:sessionId</c>.
/// </summary>
public readonly record struct SessionKey(ProviderId Provider, string SessionId)
{
    public override string ToString() => $"{Provider.Value}:{SessionId}";

    /// <summary>
    /// Parses a stored key. A text with a known provider prefix is that provider's key. Any other text is a bare session id from
    /// before stage A5, and it is Claude's (the library's bare ids were all Claude ids).
    /// </summary>
    public static SessionKey Parse(string text)
    {
        int colon = text.IndexOf(':');
        if (colon > 0)
        {
            string prefix = text[..colon];
            if (prefix == ProviderId.Claude.Value || prefix == ProviderId.Codex.Value)
            {
                return new SessionKey(new ProviderId(prefix), text[(colon + 1)..]);
            }
        }
        return new SessionKey(ProviderId.Claude, text);
    }
}
