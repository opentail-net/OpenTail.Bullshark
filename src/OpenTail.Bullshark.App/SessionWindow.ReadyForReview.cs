using System.IO;
using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The "Ready for review" lane (ready-for-review-2026-11-18): the finished coders the owner has not looked at yet, in their own rail section
/// under Needs you. The list is pure (<see cref="ReviewQueue"/>). Each pass reads the transcripts of the finished coders on a pool thread,
/// so the rail is drawn from memory. The owner's marks are kept in ui.json under <c>reviewed</c>. Nothing is sent to a session, no sound plays,
/// nothing flashes, and the section is not counted in Needs you.
/// </summary>
public partial class SessionWindow
{
    private const string ReadyKey = "ready-for-review";
    private const string ReviewedKey = "reviewed";

    /// <summary>The transcript facts of the finished coders, as the last pass read them. A coder not in it is not ready. UI thread only.</summary>
    private IReadOnlyDictionary<string, ReviewSource> _reviewSources = new Dictionary<string, ReviewSource>(StringComparer.Ordinal);

    /// <summary>The reviewed marks: session id to the last activity the owner looked at. Read from ui.json once, then kept here. UI thread only.</summary>
    private readonly Dictionary<string, DateTimeOffset> _reviewed = new(StringComparer.Ordinal);

    /// <summary>The ready coders, oldest finish first, as the last rail refresh drew them. UI thread only.</summary>
    private IReadOnlyList<ReviewItem> _review = [];

    /// <summary>Reads the reviewed marks from ui.json. A missing or unreadable file gives none, so every finished coder is shown once.</summary>
    private void InitReview()
    {
        foreach (var pair in ReviewedMarks.Parse(UiFile.Read(_options.StatePath, ReviewedKey))) _reviewed[pair.Key] = pair.Value;
    }

    /// <summary>
    /// The transcript facts of the finished coders, read on a pool thread during a pass: the files their transcripts say they wrote, and when the
    /// transcript was last written (the time of their last record). Only a coder listed as done is read. A transcript that cannot be read leaves its
    /// coder out, so it is not ready, and the rest are read. Touches no window object.
    /// </summary>
    private Dictionary<string, ReviewSource> ReviewSourcesOf(
        IReadOnlyDictionary<string, SessionRecord> records,
        IReadOnlyDictionary<string, string> paths,
        IReadOnlyList<AgentInfo> agents)
    {
        var sources = new Dictionary<string, ReviewSource>(StringComparer.Ordinal);
        foreach (var record in records.Values)
        {
            if (record.Role != SessionRole.Coder || record.SessionId.Length == 0) continue;
            if (!paths.TryGetValue(record.SessionId, out var path)) continue;
            var listed = agents.FirstOrDefault(a => SameId(a.SessionId, record.SessionId));
            if (!string.Equals(listed?.EffectiveState, "done", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var files = ReadWrites(record.SessionId, path).Files.Count;
                var lastActivity = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
                sources[record.SessionId] = new ReviewSource(files, lastActivity);
            }
            // Any failure is this session's alone: the others are still read.
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLog.Write($"review: transcript not read: {ex.GetType().Name}");
            }
        }
        return sources;
    }

    /// <summary>
    /// The Ready for review group: one row per ready coder, oldest finish first. It sits under Needs you and is shown with the live sessions only.
    /// Its header counts the rows under it.
    /// </summary>
    private TreeViewItem ReadySection(IReadOnlyDictionary<string, SessionRecord> records)
    {
        var section = NewItem(Label($"Ready for review ({_review.Count})", bold: true),
            new RailTag(RailKind.Heading, ReadyKey),
            "Coders that finished with changes and that you have not looked at yet. Opening their Changes tab, or Mark reviewed, hides them " +
            "until they do more work. Nothing is sent to the session.");
        section.IsExpanded = _expanded.GetValueOrDefault(ReadyKey, true);
        foreach (var item in _review)
        {
            if (!records.TryGetValue(item.Name, out var record)) continue;
            section.Items.Add(ReadyRow(item, record));
        }
        return section;
    }

    /// <summary>
    /// A ready coder's row: its title, and its file count and finish time on the second line. A click selects it as any session row. The menu
    /// marks it reviewed, opens its Changes tab (which also marks it), or opens its session tab (which does not).
    /// </summary>
    private TreeViewItem ReadyRow(ReviewItem item, SessionRecord record)
    {
        var files = $"{item.FilesWritten} {(item.FilesWritten == 1 ? "file" : "files")} · finished {item.FinishedAt.ToLocalTime():HH:mm}";
        var sessionId = record.SessionId;
        var row = NewItem(
            ColourEdge(TwoLine(LiveTitle(record), files, string.Empty, badge: MatchBadge(MatchOf(sessionId))), MarkOf(sessionId)?.Colour),
            new RailTag(RailKind.Session, sessionId),
            NoteTip(MarkOf(sessionId)) + SearchTip(MatchOf(sessionId), sessionId));
        row.ContextMenu = ReadyMenu(record);
        return row;
    }

    private ContextMenu ReadyMenu(SessionRecord record)
    {
        var menu = StyledMenu();
        menu.Items.Add(MenuItemFor("Mark reviewed", () => MarkReviewed(record.SessionId)));
        menu.Items.Add(MenuItemFor("Open Changes", () => OpenChanges(record.SessionId, LiveTitle(record))));
        menu.Items.Add(MenuItemFor("Open session tab", () => GoToSession(record.SessionId, LiveTitle(record))));
        return menu;
    }

    /// <summary>
    /// The owner looked at a coder's result. It is marked at its last activity as the last pass read it, or now when the pass read nothing for it.
    /// The coder leaves the lane until it does more work. The mark is written to ui.json; nothing is sent to the session.
    /// </summary>
    private void MarkReviewed(string sessionId, bool redraw = true)
    {
        var at = _reviewSources.TryGetValue(sessionId, out var source) ? source.LastActivity : DateTimeOffset.UtcNow;
        _reviewed[sessionId] = at;
        // The marks are read again under the lock and this one is added (reliability batch R5), so a mark another window made is kept. The
        // marks the file holds then are kept here too, so the rail agrees with the file.
        Dictionary<string, DateTimeOffset>? written = null;
        UiFile.Update(_options.StatePath, ReviewedKey, node =>
        {
            var marks = ReviewedMarks.Parse(node);
            marks[sessionId] = at;
            written = marks;
            return ReviewedMarks.Write(marks);
        });
        if (written is not null)
            foreach (var pair in written) _reviewed[pair.Key] = pair.Value;
        AppLog.Write("ready for review: a session marked reviewed");
        if (redraw) RefreshRail();
    }

    /// <summary>The palette's "Mark all ready sessions reviewed": each ready coder is marked, the file is written once per coder, and the rail is drawn once.</summary>
    private void MarkAllReviewed()
    {
        foreach (var item in _review) MarkReviewed(item.SessionId, redraw: false);
        RefreshRail();
    }
}
