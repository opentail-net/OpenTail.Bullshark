using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenTail.Bullshark.App;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Tabs;
using Xunit;
using Tab = OpenTail.Bullshark.App.SessionWindow.OpenTab;
using Kind = OpenTail.Bullshark.App.SessionWindow.TabKind;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// Stage C6 (App part, a)-(c): with the dropdown at Copilot, a Claude tab keeps Claude's key and a Copilot tab gets Copilot's; the same session id under
/// the two providers is two keys; and the tab layout written under Copilot holds no Copilot tab and reads back unchanged. The window itself is not built
/// here (it starts supervision and timers); the key and layout rules are the static members the window uses.
/// </summary>
public sealed class CopilotProviderTabTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-provider-tabs-" + Guid.NewGuid().ToString("N"));

    public CopilotProviderTabTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void AClaudeTabKeepsClaudesKeyAndANewCopilotTabGetsCopilotsKey()
    {
        // The window's provider is Copilot here: a tab opened under Claude still records Claude, so its key is Claude's.
        var claudeTab = new Tab(Kind.Session, "a1b2c3d4-0000-4000-8000-000000000001", "Claude one", ProviderId.Claude);
        var copilotTab = new Tab(Kind.Session, "0190a2b3-c4d5-7e6f-8a9b-0c1d2e3f4a5b", "Copilot one", ProviderId.Copilot);

        Assert.Equal(new SessionKey(ProviderId.Claude, claudeTab.Key), SessionWindow.KeyOf(claudeTab));
        Assert.Equal(new SessionKey(ProviderId.Copilot, copilotTab.Key), SessionWindow.KeyOf(copilotTab));
    }

    [Fact]
    public void TheSameSessionIdUnderClaudeAndCopilotIsTwoKeysInTheWindowsDictionaries()
    {
        const string id = "0190a2b3-c4d5-7e6f-8a9b-0c1d2e3f4a5b";
        var claude = new SessionKey(ProviderId.Claude, id);
        var copilot = new SessionKey(ProviderId.Copilot, id);
        var terminals = new Dictionary<SessionKey, string> { [claude] = "claude terminal", [copilot] = "copilot terminal" };

        Assert.NotEqual(claude, copilot);
        Assert.Equal(2, terminals.Count);
        Assert.Equal("claude terminal", terminals[SessionWindow.KeyOf(new Tab(Kind.Session, id, "Claude", ProviderId.Claude))]);
        Assert.Equal("copilot terminal", terminals[SessionWindow.KeyOf(new Tab(Kind.Session, id, "Copilot", ProviderId.Copilot))]);
    }

    [Fact]
    public void ALayoutWrittenUnderCopilotHoldsNoCopilotTab()
    {
        var tabs = new List<Tab>
        {
            new(Kind.Setup, "setup", "New session", ProviderId.Copilot),
            new(Kind.Session, "a1b2c3d4-0000-4000-8000-000000000001", "Claude one", ProviderId.Claude),
            new(Kind.Session, "0190a2b3-c4d5-7e6f-8a9b-0c1d2e3f4a5b", "Copilot one", ProviderId.Copilot),
            new(Kind.Session, "a1b2c3d4-0000-4000-8000-000000000002", "Claude two", ProviderId.Claude),
        };
        var rightIds = new HashSet<string>(StringComparer.Ordinal) { "a1b2c3d4-0000-4000-8000-000000000002" };

        var claude = TabLayoutFile.ClaudeSessionTabs(tabs);
        var order = claude.Select(t => t.Key).ToList();
        var right = claude.Where(t => rightIds.Contains(t.Key)).Select(t => t.Key).ToList();
        var layout = TabLayoutFile.ToFile(order, right);

        Assert.Equal(new[] { "claude:a1b2c3d4-0000-4000-8000-000000000001", "claude:a1b2c3d4-0000-4000-8000-000000000002" }, layout.Order.ToArray());
        Assert.Equal(new[] { "claude:a1b2c3d4-0000-4000-8000-000000000002" }, layout.Right.ToArray());
        Assert.DoesNotContain(layout.Order, key => key.Contains("0190a2b3", StringComparison.Ordinal));
    }

    [Fact]
    public void ALayoutWithNoCopilotTabReadsBackUnchangedThroughTheFile()
    {
        var order = new[] { "a1b2c3d4-0000-4000-8000-000000000002", "a1b2c3d4-0000-4000-8000-000000000001" };
        var right = new[] { "a1b2c3d4-0000-4000-8000-000000000001" };
        var path = Path.Combine(_dir, "tabs.json");

        TabOrder.Save(path, TabLayoutFile.ToFile(order, right));
        var read = TabOrder.Load(path);

        Assert.Equal(order, TabLayoutFile.ClaudeIds(read.Order).ToArray());
        Assert.Equal(right, TabLayoutFile.ClaudeIds(read.Right).ToArray());
    }

    [Fact]
    public void AnOlderBareIdFileReadsAsClaudeAndACopilotKeyIsLeftOut()
    {
        var stored = new[] { "a1b2c3d4-0000-4000-8000-000000000001", "claude:a1b2c3d4-0000-4000-8000-000000000002", "copilot:0190a2b3-c4d5-7e6f-8a9b-0c1d2e3f4a5b" };

        Assert.Equal(
            new[] { "a1b2c3d4-0000-4000-8000-000000000001", "a1b2c3d4-0000-4000-8000-000000000002" },
            TabLayoutFile.ClaudeIds(stored).ToArray());
        Assert.Empty(TabLayoutFile.ClaudeIds(Array.Empty<string>()));
    }
}
