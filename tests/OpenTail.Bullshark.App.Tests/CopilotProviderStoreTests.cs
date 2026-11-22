using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using OpenTail.Bullshark.App;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// Stage C6 (App part, d): the <c>provider</c> key in ui.json. A missing key, and any value that is neither <c>codex</c> nor <c>copilot</c>, is Claude; <c>copilot</c> is Copilot (G2.1);
/// saving keeps the other keys. The state file is in a temporary folder, and the app log follows it there, so the owner's files are not touched.
/// </summary>
public sealed class CopilotProviderStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-provider-store-" + Guid.NewGuid().ToString("N"));
    private readonly string _state;

    public CopilotProviderStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _state = Path.Combine(_dir, "state.json");
        AppLog.FollowStateFile(_state);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string UiPath => UiFile.PathFor(_state);

    private void WriteUi(string json) => File.WriteAllText(UiPath, json);

    private JsonObject ReadUi() => JsonNode.Parse(File.ReadAllText(UiPath))!.AsObject();

    [Fact]
    public void WithNoUiFileTheProviderIsClaude()
    {
        Assert.False(File.Exists(UiPath));

        Assert.Equal(ProviderId.Claude, ProviderStore.Load(_state));
    }

    [Fact]
    public void WithNoProviderKeyTheProviderIsClaudeAndTheOtherKeysAreUnchanged()
    {
        WriteUi("{\"zoomPercent\": 120, \"railTheme\": \"Light\"}");

        Assert.Equal(ProviderId.Claude, ProviderStore.Load(_state));
    }

    [Fact]
    public void CopilotIsCopilot()
    {
        WriteUi("{\"provider\": \"copilot\"}");

        Assert.Equal(ProviderId.Copilot, ProviderStore.Load(_state));
    }

    [Theory]
    [InlineData("\"claude\"")]
    [InlineData("\"opencode\"")]
    [InlineData("\"\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("{\"name\": \"copilot\"}")]
    public void AnyOtherValueIsClaude(string json)
    {
        WriteUi("{\"provider\": " + json + "}");

        Assert.Equal(ProviderId.Claude, ProviderStore.Load(_state));
    }

    [Fact]
    public void SavingCopilotKeepsTheOtherKeysAndLoadsBack()
    {
        WriteUi("{\"zoomPercent\": 120, \"railTheme\": \"Light\"}");

        ProviderStore.Save(_state, ProviderId.Copilot);

        var root = ReadUi();
        Assert.Equal("copilot", root["provider"]!.GetValue<string>());
        Assert.Equal(120, root["zoomPercent"]!.GetValue<int>());
        Assert.Equal("Light", root["railTheme"]!.GetValue<string>());
        Assert.Equal(ProviderId.Copilot, ProviderStore.Load(_state));
    }

    [Fact]
    public void SavingClaudeAfterCopilotWritesClaudeAndKeepsTheOtherKeys()
    {
        WriteUi("{\"zoomPercent\": 90, \"provider\": \"copilot\"}");

        ProviderStore.Save(_state, ProviderId.Claude);

        var root = ReadUi();
        Assert.Equal("claude", root["provider"]!.GetValue<string>());
        Assert.Equal(90, root["zoomPercent"]!.GetValue<int>());
        Assert.Equal(ProviderId.Claude, ProviderStore.Load(_state));
    }

    [Fact]
    public void SavingIntoNoFileCreatesOneWithTheProviderOnly()
    {
        ProviderStore.Save(_state, ProviderId.Copilot);

        var root = ReadUi();
        Assert.Single(root);
        Assert.Equal("copilot", root["provider"]!.GetValue<string>());
    }
}
