using System.Globalization;
using System.Text.Json.Nodes;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>One re-alert for a session that still waits: the item, and the number of this re-alert in its count (1 for the first after a snooze).</summary>
public sealed record ReAlert(AttentionItem Item, int Again);

/// <summary>
/// Re-alerts for the Needs-you items the owner has not answered (escalation-2026-11-18, D1 to D6). A session that stays in the list for a
/// reason the owner must act on sounds again every <c>escalateMinutes</c>, at most <see cref="MaxReAlerts"/> times in a count. The owner can
/// acknowledge it (no more re-alerts until it stops waiting) or snooze it (none until a time, then one, and the count starts again).
/// Nothing is sent to a session: this class only says which items are due, and the window sounds and flashes.
/// It is pure: the time is passed in, and its state lives in memory for the life of the window. Used on the UI thread only.
/// </summary>
public sealed class AttentionEscalation
{
    /// <summary>The re-alerts allowed in one count: after the first announcement, or after a snooze has ended (D2).</summary>
    public const int MaxReAlerts = 3;

    /// <summary>The interval when ui.json has no usable <c>escalateMinutes</c> (D2).</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(15);

    private static readonly double LongestIntervalMinutes = 1440;

    private readonly Dictionary<string, Spell> _spells = new(StringComparer.Ordinal);

    /// <summary>One waiting spell, from the first time its item is seen until the item leaves the list (D6: kept by name).</summary>
    private sealed class Spell(DateTimeOffset firstSeen)
    {
        public DateTimeOffset LastAlert { get; set; } = firstSeen;

        /// <summary>The re-alerts given in the current count.</summary>
        public int ReAlerts { get; set; }

        public DateTimeOffset? SnoozedUntil { get; set; }

        public bool Acknowledged { get; set; }
    }

    /// <summary>
    /// Only the reasons the owner must act on escalate (D1): blocked, restart loop, start failed, stopped, failed, and name taken (lifecycle batch
    /// L1, with the restart loop it stops). The notices (overlap, conflict, limit reached, silent, usage limit) never re-alert.
    /// </summary>
    public static bool Escalates(AttentionItem item) =>
        item.Reason is AttentionReason.Blocked or AttentionReason.RestartLoop or AttentionReason.StartFailed
            or AttentionReason.Stopped or AttentionReason.Failed or AttentionReason.NameTaken;

    /// <summary>
    /// The interval from ui.json's <c>escalateMinutes</c>. A missing value, a value that is not a number, or a negative one gives
    /// <see cref="DefaultInterval"/>. Zero is off (<see cref="TimeSpan.Zero"/>). Any other number is clamped to 1 to 1440 minutes.
    /// </summary>
    public static TimeSpan IntervalOf(JsonNode? value)
    {
        // The node's JSON text, so a number made in code (an int) and one read from the file are read alike; a string or a bool is not a number.
        if (value is not JsonValue json
            || !double.TryParse(json.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes)
            || minutes < 0)
            return DefaultInterval;
        if (minutes == 0) return TimeSpan.Zero;
        return TimeSpan.FromMinutes(Math.Clamp(minutes, 1, LongestIntervalMinutes));
    }

    /// <summary>
    /// The state after one pass. <paramref name="current"/> is the whole Needs-you list. An escalating item with no spell starts one at
    /// <paramref name="now"/>: that is its first announcement, or the baseline of the first pass, so it is not due now. Returns the items
    /// due for a re-alert at <paramref name="now"/>. With an interval of zero nothing is due; the spells are kept, so turning it back on
    /// counts from their last alert.
    /// </summary>
    public IReadOnlyList<ReAlert> Update(IReadOnlyList<AttentionItem> current, DateTimeOffset now, TimeSpan interval)
    {
        var waiting = current.Where(Escalates).DistinctBy(item => item.Name, StringComparer.Ordinal).ToList();
        var names = waiting.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);

        // A spell ends when its item leaves the list. If the item comes back, it is a new spell, announced as usual.
        foreach (var name in _spells.Keys.Where(name => !names.Contains(name)).ToList())
            _spells.Remove(name);

        var due = new List<ReAlert>();
        foreach (var item in waiting)
        {
            if (!_spells.TryGetValue(item.Name, out var spell))
            {
                _spells[item.Name] = new Spell(now);
                continue;
            }
            if (interval <= TimeSpan.Zero || spell.Acknowledged) continue;

            if (spell.SnoozedUntil is { } until)
            {
                if (now < until) continue;

                // The snooze has ended and the item still waits: one re-alert, and the count starts again at this one (D4).
                spell.SnoozedUntil = null;
                spell.ReAlerts = 1;
                spell.LastAlert = now;
                due.Add(new ReAlert(item, 1));
                continue;
            }

            if (spell.ReAlerts >= MaxReAlerts || now - spell.LastAlert < interval) continue;
            spell.ReAlerts++;
            spell.LastAlert = now;
            due.Add(new ReAlert(item, spell.ReAlerts));
        }
        return due;
    }

    /// <summary>
    /// The owner acknowledged the item (D3): no more re-alerts for it until it stops waiting. It stays in the list. Clears a snooze.
    /// A name with no spell is ignored.
    /// </summary>
    public void Acknowledge(string name)
    {
        if (!_spells.TryGetValue(name, out var spell)) return;
        spell.Acknowledged = true;
        spell.SnoozedUntil = null;
    }

    /// <summary>
    /// The owner snoozed the item until <paramref name="until"/> (D4): no re-alert before then, and one at the end if it still waits.
    /// It takes back an acknowledgement, so the snooze's own re-alert is not swallowed. A name with no spell is ignored.
    /// </summary>
    public void Snooze(string name, DateTimeOffset until)
    {
        if (!_spells.TryGetValue(name, out var spell)) return;
        spell.Acknowledged = false;
        spell.SnoozedUntil = until;
    }
}
