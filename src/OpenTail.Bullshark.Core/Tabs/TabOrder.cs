using System.Text.Json;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Tabs;

/// <summary>
/// The order of the session tabs (docs/plans/tabs-and-panes.md, T1). The owner drags a tab to a new place; the order of the
/// session tabs is kept in tabs.json, beside the state file, with the sessions shown in the right pane (T2). A file that is missing
/// or cannot be read gives no order, and the tabs then open in name order as before.
/// </summary>
public static class TabOrder
{
    /// <summary>The file that holds the order, in the folder of the state file.</summary>
    public static string PathBeside(string stateFile) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(stateFile))!, "tabs.json");

    /// <summary>
    /// Moves the item at <paramref name="from"/> to <paramref name="to"/>, where both are places in the list as it stands.
    /// Returns false, and changes nothing, when either place is outside the list or they are the same.
    /// </summary>
    public static bool Move<T>(IList<T> items, int from, int to)
    {
        if (from < 0 || to < 0 || from >= items.Count || to >= items.Count || from == to) return false;
        var item = items[from];
        items.RemoveAt(from);
        items.Insert(to, item);
        return true;
    }

    /// <summary>
    /// The items in the saved order. An item whose id is not saved keeps its place among the other unsaved items, after
    /// every saved one, so a new session's tab opens last.
    /// </summary>
    public static IReadOnlyList<T> Arrange<T>(IEnumerable<T> items, Func<T, string> idOf, IReadOnlyList<string> saved)
    {
        var place = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < saved.Count; i++) place.TryAdd(saved[i], i);
        return items.OrderBy(item => place.TryGetValue(idOf(item), out var at) ? at : int.MaxValue).ToList();
    }

    /// <summary>
    /// Which tab a dragged tab should change places with, or -1 for none. The pointer must pass the middle of the other tab,
    /// in the direction of the drag, so two tabs of different widths do not swap back and forth under a still pointer.
    /// <paramref name="spans"/> gives the left and right edge of each tab, in strip order.
    /// </summary>
    public static int DropPlace(IReadOnlyList<(double Left, double Right)> spans, int dragged, double pointer)
    {
        if (dragged < 0 || dragged >= spans.Count) return -1;
        for (var i = 0; i < spans.Count; i++)
        {
            if (i == dragged || pointer < spans[i].Left || pointer >= spans[i].Right) continue;
            var middle = (spans[i].Left + spans[i].Right) / 2;
            return i > dragged ? (pointer > middle ? i : -1) : (pointer < middle ? i : -1);
        }
        return -1;
    }

    /// <summary>The saved layout. Empty when the file is missing or cannot be read.</summary>
    public static TabLayout Load(string path, Action<string>? log = null)
    {
        try
        {
            if (!File.Exists(path)) return TabLayout.Empty;
            var read = JsonSerializer.Deserialize<TabLayout>(File.ReadAllText(path));
            return new TabLayout(read?.Order ?? [], read?.Right ?? []);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            log?.Invoke($"tab layout not read: {ex.Message}");
            return TabLayout.Empty;
        }
    }

    /// <summary>Saves the layout. A file that cannot be written is logged; the layout then lasts until the window closes.</summary>
    public static void Save(string path, TabLayout layout, Action<string>? log = null)
    {
        try
        {
            FileLock.WriteAtomic(path, JsonSerializer.Serialize(layout));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"tab layout not saved: {ex.Message}");
        }
    }
}

/// <summary>
/// What tabs.json holds: the session tabs in strip order, and the sessions shown in the right pane when the pane is split (T2).
/// Both are lists of session ids.
/// </summary>
public sealed record TabLayout(IReadOnlyList<string> Order, IReadOnlyList<string> Right)
{
    public static TabLayout Empty { get; } = new([], []);
}
