namespace OpenTail.Bullshark.Core.Cache;

/// <summary>The lifetime of a session's cache, read from its cache writes (ephemeral_1h or ephemeral_5m).</summary>
public enum CacheTtl
{
    OneHour,
    FiveMinutes,

    /// <summary>No cache write was seen, so the lifetime is not known. It is treated as one hour, and the label says so.</summary>
    Unknown,
}

public enum CacheState
{
    Warm,
    Cooling,
    Cold,
    Unknown,
}

/// <summary>Where a session's cache stands now, and what a message would cost in tokens.</summary>
public sealed record CacheVerdict(CacheState State, TimeSpan Idle, CacheTtl Ttl, long ContextTokens);

/// <summary>
/// The warm and cold rule (D-S7). The cache lives for its lifetime; a message sent inside the warm part reads the cached
/// context, and one sent after the cold part rewrites it. The margin is 10 minutes before the end of each lifetime, so
/// the label is not wrong near the boundary. Money is never shown (F17): the labels give tokens only.
/// </summary>
public static class CacheRule
{
    /// <summary>The warm and cold limits for a lifetime. One hour: 50 and 60 minutes. Five minutes: 4 and 5 minutes.</summary>
    public static (TimeSpan Warm, TimeSpan Cold) Thresholds(CacheTtl ttl) => ttl == CacheTtl.FiveMinutes
        ? (TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5))
        : (TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(60));

    /// <param name="lastRequest">The time of the last request (a message that read or wrote the cache), or null when there is none.</param>
    public static CacheVerdict Classify(DateTimeOffset? lastRequest, DateTimeOffset now, CacheTtl ttl, long contextTokens)
    {
        if (lastRequest is null) return new CacheVerdict(CacheState.Unknown, TimeSpan.Zero, ttl, contextTokens);

        var idle = now - lastRequest.Value;
        if (idle < TimeSpan.Zero) idle = TimeSpan.Zero;
        var (warm, cold) = Thresholds(ttl);
        var state = idle < warm ? CacheState.Warm : idle < cold ? CacheState.Cooling : CacheState.Cold;
        return new CacheVerdict(state, idle, ttl, contextTokens);
    }

    /// <summary>The short form for a rail row: the state and the idle minutes.</summary>
    public static string Short(CacheVerdict verdict) => verdict.State switch
    {
        CacheState.Warm => $"warm {Minutes(verdict.Idle)}",
        CacheState.Cooling => $"cooling {Minutes(verdict.Idle)}",
        CacheState.Cold => $"cold {Minutes(verdict.Idle)}",
        _ => "no request yet",
    };

    /// <summary>The full label, for a tooltip. Tokens only.</summary>
    public static string Label(CacheVerdict verdict)
    {
        var lifetime = verdict.Ttl == CacheTtl.Unknown ? " (lifetime not seen; one hour assumed)" : string.Empty;
        return verdict.State switch
        {
            CacheState.Warm => $"Warm, {Minutes(verdict.Idle)} idle{lifetime}: a message reads about {verdict.ContextTokens:N0} cached tokens.",
            CacheState.Cooling => $"Cooling, {Minutes(verdict.Idle)} idle{lifetime}: a message may rewrite about {verdict.ContextTokens:N0} tokens.",
            CacheState.Cold => $"Cold, {Minutes(verdict.Idle)} idle{lifetime}: a message rewrites about {verdict.ContextTokens:N0} tokens.",
            _ => "No request yet.",
        };
    }

    private static string Minutes(TimeSpan span) => $"{(int)span.TotalMinutes} min";
}
