namespace OpenTail.Bullshark.Core.Nudge;

public sealed record NudgeDecision(bool Allowed, string Reason)
{
    public static NudgeDecision Allow(string reason) => new(true, reason);
    public static NudgeDecision Deny(string reason) => new(false, reason);
}

/// <summary>
/// Decides whether OpenTail.Bullshark may type a line into a session's tab (decision D5, design principle 3).
/// The rules are: never while the owner is using the tab, never into a session that is waiting for a
/// permission answer, and only into a session that is idle, so the line is read as a new turn.
/// </summary>
public static class NudgePolicy
{
    public static readonly TimeSpan DefaultOwnerIdle = TimeSpan.FromMinutes(10);

    public static NudgeDecision Evaluate(
        DateTimeOffset now,
        DateTimeOffset? lastOwnerInput,
        string? state,
        TimeSpan ownerIdle)
    {
        if (lastOwnerInput is { } typed && now - typed < ownerIdle)
            return NudgeDecision.Deny($"the owner typed in this tab {Math.Round((now - typed).TotalMinutes, 1)} min ago");

        return state?.ToLowerInvariant() switch
        {
            // Typing into a prompt would answer a permission question on the owner's behalf. Never do that.
            "blocked" => NudgeDecision.Deny("the session is waiting for a permission answer"),
            "working" => NudgeDecision.Deny("the session is mid-turn; wait for it to finish"),
            "done" or "busy" => NudgeDecision.Allow("the session is idle and the owner has not typed recently"),
            _ => NudgeDecision.Deny($"state '{state ?? "unknown"}' is not known to be idle"),
        };
    }
}
