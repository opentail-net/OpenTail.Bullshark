using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Ui;

namespace OpenTail.Bullshark.App;

/// <summary>
/// When the rail is drawn again (perf and testability batch, Q5). A pass that finds what the rail draws unchanged keeps the rows: the scroll
/// position and the selection stay, and the rail's work on the UI thread is saved. The signature lists every input the rows are drawn from, the
/// minute included for the clock-dependent text. A refresh from a user action always draws again, and records its signature for the next pass.
/// Expanded folders are not in the signature: a toggle is already on the rows, and the stored state is read again on each build.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The signature of the rail as it was last drawn. Null after a build that did not finish, so the next pass draws again.</summary>
    private string? _railSignature;

    /// <summary>The passes that found the rail unchanged and kept its rows (soak log).</summary>
    private int _railsKept;

    /// <summary>The rebuilds that were made while the rail was scrolled down: each one put the scroll back to the top (soak log).</summary>
    private int _railRebuildsWhileScrolled;

    /// <summary>
    /// The history's version: it changes each time the history is read, so the signature sees a new history without comparing its sessions
    /// (perf and testability batch, Q5). UI thread only.
    /// </summary>
    private int _historyVersion;

    /// <summary>
    /// The signature of everything the rail draws, or null when one part cannot be written (the rail is then drawn again: the error is logged once).
    /// </summary>
    private string? RailSignatureNow(
        IReadOnlyDictionary<string, SessionRecord> records,
        IReadOnlyList<RunRecord> runs,
        DateTimeOffset now,
        PassSettings settings)
    {
        try
        {
            return new RailSignature()
                .Add("theme", _theme.ToString())
                .Add("filter", _filter.ToString())
                .Add("tag", ActiveTagFilter)
                .Add("groupByTag", _groupByTag ? "1" : "0")
                .Add("libraryError", _libraryError)
                .Add("watching", _watching ? "1" : "0")
                .Add("daemonNotice", _daemonNotice)
                .Add("problem", _passHealth.Problem)
                .Add("pinnedGroup", PinnedGroupShown ? "1" : "0")
                .Add("history", _history is null ? "-" : _historyVersion.ToString(CultureInfo.InvariantCulture))
                .Add("historyLoading", _historyLoading ? "1" : "0")
                .Add("contextWindow", settings.ContextWindow.ToString(CultureInfo.InvariantCulture))
                .Add("silentAfter", settings.SilentAfter.Ticks.ToString(CultureInfo.InvariantCulture))
                .Add("minute", RailSignature.MinuteOf(now))
                .AddJson("records", records.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).ToList())
                .AddJson("runs", runs)
                .AddJson("agents", _agents)
                .AddJson("attention", _attention)
                .AddJson("review", _review)
                .AddJson("marks", _marks.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList())
                .AddJson("running", _runningFacts.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList())
                .AddJson("clashes", _clashes)
                .AddJson("merges", _merges.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList())
                .AddJson("trust", _trustSteps.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList())
                .AddJson("plans", _marks.Values.Where(mark => mark.PlanFile is not null)
                    .Select(mark => _planProgress.Get(mark.PlanFile!)).ToList())
                .AddJson("search", _searchMatches?.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new
                    {
                        pair.Key,
                        pair.Value.MatchingMessages,
                        Hits = pair.Value.Hits.Select(hit => new { hit.LineNumber, hit.Snippet }).ToList(),
                    }).ToList())
                .Build();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"rail signature not made, the rail is drawn again: {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>The rail's vertical scroll position, in device-independent pixels: zero when there is no scroll viewer yet.</summary>
    private double RailScrollOffset() => FindScrollViewer(Rail)?.VerticalOffset ?? 0;

    /// <summary>Scrolls the rail down to an offset. Used by the test flag --rail-scroll only, so a rebuild's effect on the scroll can be logged.</summary>
    private void ScrollRailTo(double offset) => FindScrollViewer(Rail)?.ScrollToVerticalOffset(offset);

    private static ScrollViewer? FindScrollViewer(DependencyObject? parent)
    {
        if (parent is null) return null;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer viewer) return viewer;
            if (FindScrollViewer(child) is { } found) return found;
        }
        return null;
    }
}
