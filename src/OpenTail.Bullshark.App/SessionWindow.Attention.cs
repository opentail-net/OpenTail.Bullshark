using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The sessions that need the owner (item 1 of the additions plan): the "Needs you" group at the top of the rail, the count in
/// the status bar, and the announcement when a session starts waiting. The lists are computed in Core; this file only draws them.
/// </summary>
public partial class SessionWindow
{
    private const string NeedsYouKey = "needs-you";

    /// <summary>The sessions that need the owner, as the last refresh saw them. Shown under Needs you and counted in the status bar.</summary>
    private IReadOnlyList<AttentionItem> _attention = [];

    /// <summary>
    /// The list the last pass announced from. Null until the first pass after the window opens: the sessions that are already
    /// waiting then are in view, so they are not announced.
    /// </summary>
    private IReadOnlyList<AttentionItem>? _announced;

    private void UpdateNeedsYouCount()
    {
        var count = _attention.Count;
        NeedsYouText.Text = count == 1 ? "1 session needs you" : $"{count} sessions need you";
        NeedsYouText.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// The Needs you group: one row per waiting session, oldest first. A session with a terminal gets the same row as under its
    /// folder, so a click selects it as usual. A start that never got a session has no terminal and shows its reason, as a folder does.
    /// </summary>
    private TreeViewItem NeedsYouSection(IReadOnlyDictionary<string, SessionRecord> records)
    {
        var section = NewItem(Label($"Needs you ({_attention.Count})", bold: true),
            new RailTag(RailKind.Heading, NeedsYouKey),
            "Sessions that wait for you: blocked on a permission or a question, stopped and not brought back, or a start that failed.");
        section.IsExpanded = _expanded.GetValueOrDefault(NeedsYouKey, true);

        foreach (var item in _attention)
        {
            if (!records.TryGetValue(item.Name, out var record)) continue;
            if (string.IsNullOrEmpty(record.SessionId))
            {
                section.Items.Add(NewItem(Label($"{record.Name} · {item.Detail}", muted: true),
                    new RailTag(RailKind.Heading, NeedsYouKey + ":failed:" + record.Name), item.Detail));
                continue;
            }
            var state = _agents.FirstOrDefault(a => string.Equals(a.SessionId, record.SessionId, StringComparison.OrdinalIgnoreCase))
                            ?.EffectiveState ?? "not listed";
            section.Items.Add(LiveRow(record, state));
        }
        return section;
    }

    /// <summary>
    /// Called after each pass. When a session joined the list since the last pass, the window flashes its taskbar button until it
    /// is in front and plays the notification sound once. Both are off when ui.json says "notify": false. The first pass after the
    /// window opens only sets the baseline.
    /// </summary>
    private void AnnounceAttention()
    {
        var previous = _announced;
        _announced = _attention;
        if (previous is null) return;

        var gained = AttentionList.Gained(previous, _attention);
        if (gained.Count == 0) return;
        if (!AttentionList.NotificationsOn(UiFile.Read(_options.StatePath, "notify"))) return;

        AppLog.Write($"needs you: {string.Join(", ", gained.Select(g => g.Name))}");
        SystemSounds.Asterisk.Play();
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) TaskbarFlash.UntilFocused(handle);
    }
}
