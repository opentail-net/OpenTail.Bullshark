namespace OpenTail.Bullshark.Core.Providers;

/// <summary>
/// A session's identity across providers (R-3). Two providers may use the same session id, so every dictionary, file and tag keyed
/// by a session uses this key. The text form is <c>provider:sessionId</c>.
/// </summary>
public readonly record struct SessionKey(ProviderId Provider, string SessionId)
{
    public override string ToString() => $"{Provider.Value}:{SessionId}";

    /// <summary>
    /// True when a stored text has a provider prefix: a provider id (lower-case letters, digits and hyphens, starting with a letter)
    /// then a colon. A bare session id has no colon (Claude's ids are UUIDs), so it is never taken for a prefix.
    /// </summary>
    public static bool HasProviderPrefix(string text)
    {
        int colon = text.IndexOf(':');
        if (colon <= 0 || !char.IsAsciiLetterLower(text[0])) return false;
        for (int i = 1; i < colon; i++)
        {
            char c = text[i];
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')) return false;
        }
        return true;
    }

    /// <summary>
    /// Parses a stored key. A text with a provider prefix (see <see cref="HasProviderPrefix"/>) is that provider's key, so a provider
    /// added later (for example <c>fake:</c> in the tests) parses the same way as Claude and Codex. Any other text is a bare session id
    /// from before stage A5, and it is Claude's (the library's bare ids were all Claude ids).
    /// </summary>
    public static SessionKey Parse(string text)
    {
        if (HasProviderPrefix(text))
        {
            int colon = text.IndexOf(':');
            return new SessionKey(new ProviderId(text[..colon]), text[(colon + 1)..]);
        }
        return new SessionKey(ProviderId.Claude, text);
    }
}
