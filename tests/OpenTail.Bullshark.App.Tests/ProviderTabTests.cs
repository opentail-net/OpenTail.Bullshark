using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenTail.Bullshark.App;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Tabs;
using Xunit;
using Tab = OpenTail.Bullshark.App.SessionWindow.OpenTab;
using Kind = OpenTail.Bullshark.App.SessionWindow.TabKind;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// Stage C6 (App part, a)-(c): with the dropdown at Codex, a Claude tab keeps Claude's key and a Codex tab gets Codex's; the same session id under
/// the two providers is two keys; and the tab layout written under Codex holds no Codex tab and reads back unchanged. The window itself is not built
/// here (it starts supervision and timers); the key and layout rules are the static members the window uses.
/// </summary>
public sealed class ProviderTabTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-provider-tabs-" + Guid.NewGuid().ToString("N"));

    public ProviderTabTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void CodexIsAvailableOnThisTarget()
    {
        // Codex-on-net10 (Z2): both targets read Codex history, so the dropdown offers Codex on net10.0 and on net11.0.
        Assert.True(CodexProvider.Available);
    }

    [Fact]
    public void AClaudeTabKeepsClaudesKeyAndANewCodexTabGetsCodexsKey()
    {
        // The window's provider is Codex here: a tab opened under Claude still records Claude, so its key is Claude's.
        var claudeTab = new Tab(Kind.Session, "a1b2c3d4-0000-4000-8000-000000000001", "Claude one", ProviderId.Claude);
        var codexTab = new Tab(Kind.Session, "0190a2b3-c4d5-7e6f-8a9b-0c1d2e3f4a5b", "Codex one", ProviderId.Codex);

        Assert.Equal(new SessionKey(ProviderId.Claude, claudeTab.Key), SessionWindow.KeyOf(claudeTab));
        Assert.Equal(new SessionKey(ProviderId.Codex, codexTab.Key), SessionWindow.KeyOf(codexTab));
    }

    [Fact]
    public void TheSameSessionIdUnderClaudeAndCodexIsTwoKeysInTheWindowsDictionaries()
    {
        const string id = "0190a2b3-c4d5-7e6f-8a9b-0c1d2e3f4a5b";
        var claude = new SessionKey(ProviderId.Claude, id);
        var codex = new SessionKey(ProviderId.Codex, id);
        var terminals = new Dictionary<SessionKey, string> { [claude] = "claude terminal", [codex] = "codex terminal" };

        Assert.NotEqual(claude, codex);
        Assert.Equal(2, terminals.Count);
        Assert.Equal("claude terminal", terminals[SessionWindow.KeyOf(new Tab(Kind.Session, id, "Claude", ProviderId.Claude))]);
        Assert.Equal("codex terminal", terminals[SessionWindow.KeyOf(new Tab(Kind.Session, id, "Codex", ProviderId.Codex))]);
    }

    [Fact]
    public void ALayoutWrittenUnderCodexHoldsNoCodexTab()
    {
        var tabs = new List<Tab>
        {
            new(Kind.Setup, "setup", "New session", ProviderId.Codex),
            new(Kind.Session, "a1b2c3d4-0000-4000-8000-000000000001", "Claude one", ProviderId.Claude),
            new(Kind.Session, "0190a2b3-c4d5-7e6f-8a9b-0c1d2e3f4a5b", "Codex one", ProviderId.Codex),
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
    public void ALayoutWithNoCodexTabReadsBackUnchangedThroughTheFile()
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
    public void AnOlderBareIdFileReadsAsClaudeAndACodexKeyIsLeftOut()
    {
        var stored = new[] { "a1b2c3d4-0000-4000-8000-000000000001", "claude:a1b2c3d4-0000-4000-8000-000000000002", "codex:0190a2b3-c4d5-7e6f-8a9b-0c1d2e3f4a5b" };

        Assert.Equal(
            new[] { "a1b2c3d4-0000-4000-8000-000000000001", "a1b2c3d4-0000-4000-8000-000000000002" },
            TabLayoutFile.ClaudeIds(stored).ToArray());
        Assert.Empty(TabLayoutFile.ClaudeIds(Array.Empty<string>()));
    }
}
