using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Tabs;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The provider the window shows, saved in ui.json beside the other keys (stage C4, C-D6; G2.1): <c>"provider": "claude"</c>, <c>"codex"</c> or <c>"copilot"</c>.
/// <see cref="UiFile.Set"/> keeps the other keys. A missing or unreadable value is Claude, so the window starts as before.
/// </summary>
public static class ProviderStore
{
    public static ProviderId Load(string stateFile)
    {
        var text = UiFile.Read(stateFile, ProviderChoice.Key) is JsonValue value && value.TryGetValue<string>(out var name) ? name : null;
        return ProviderChoice.Parse(text);
    }

    /// <summary>Saves the choice. A file that cannot be written is logged by <see cref="UiFile"/>; the choice then lasts until the window closes.</summary>
    public static void Save(string stateFile, ProviderId id) =>
        UiFile.Set(stateFile, ProviderChoice.Key, JsonValue.Create(ProviderChoice.TextOf(id)));
}

/// <summary>
/// The session tabs' layout in tabs.json (stage C, the open item of A5). Only Claude's session tabs are kept: a Codex tab is not restored
/// after a restart (C-D5). The file holds provider keys (<c>claude:&lt;id&gt;</c>); a bare id from the older file reads as Claude, so both
/// forms are read.
/// </summary>
internal static class TabLayoutFile
{
    /// <summary>The session tabs that belong to Claude, in strip order. A Codex tab, or any other provider's, is left out.</summary>
    public static IReadOnlyList<SessionWindow.OpenTab> ClaudeSessionTabs(IEnumerable<SessionWindow.OpenTab> tabs) =>
        tabs.Where(tab => tab.Kind == SessionWindow.TabKind.Session && tab.Provider == ProviderId.Claude).ToList();

    /// <summary>The file form of the layout: the given bare Claude ids, written with the provider prefix.</summary>
    public static TabLayout ToFile(IEnumerable<string> order, IEnumerable<string> right) =>
        new(Keys(order), Keys(right));

    /// <summary>The Claude ids of a stored list, in either form. A key of another provider is left out; a bare id is Claude's.</summary>
    public static IReadOnlyList<string> ClaudeIds(IEnumerable<string> stored) =>
        stored.Select(SessionKey.Parse).Where(key => key.Provider == ProviderId.Claude).Select(key => key.SessionId).ToList();

    private static IReadOnlyList<string> Keys(IEnumerable<string> ids) =>
        ids.Select(id => new SessionKey(ProviderId.Claude, id).ToString()).ToList();
}
