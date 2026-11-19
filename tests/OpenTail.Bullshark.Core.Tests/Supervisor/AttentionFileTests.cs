using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>The attention file (firstrun batch B9): off by default, reason codes and names only, UTC time, atomic write.</summary>
public sealed class AttentionFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "bullshark-fr-" + Guid.NewGuid().ToString("N")[..8]);

    public AttentionFileTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static AttentionItem Item(string name, AttentionReason reason, string detail) =>
        new(name, "0a1b2c3d-1111-2222-3333-444455556666", reason, detail, DateTimeOffset.UnixEpoch);

    [Fact]
    public void TheSwitchIsOffUnlessTheOwnerTurnedItOn()
    {
        Assert.False(AttentionFile.Enabled(null));
        Assert.False(AttentionFile.Enabled(JsonValue.Create(false)));
        Assert.False(AttentionFile.Enabled(JsonValue.Create("true")));
        Assert.False(AttentionFile.Enabled(JsonValue.Create(1)));
        Assert.True(AttentionFile.Enabled(JsonValue.Create(true)));
    }

    [Fact]
    public void TheFileHoldsTheTimeCountAndReasonCodesAndNames()
    {
        var at = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.FromHours(3));
        var items = new[]
        {
            Item("planner", AttentionReason.Blocked, "waiting for a permission answer: rm -rf secret"),
            Item("coder", AttentionReason.Failed, @"a detail with C:\Users\someone\x"),
        };

        var root = JsonNode.Parse(AttentionFile.Build(at, items))!.AsObject();

        Assert.Equal(at.ToUniversalTime().ToString("O"), root["at"]!.GetValue<string>());
        Assert.Equal(2, root["count"]!.GetValue<int>());
        var first = root["items"]![0]!.AsObject();
        Assert.Equal("Blocked", first["reason"]!.GetValue<string>());
        Assert.Equal("planner", first["name"]!.GetValue<string>());
        Assert.Equal(new[] { "name", "reason" }, first.Select(pair => pair.Key).OrderBy(key => key, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void NoDetailTextOrSessionIdIsWritten()
    {
        var text = AttentionFile.Build(DateTimeOffset.UtcNow, [Item("planner", AttentionReason.Blocked, @"CANARYDETAIL and C:\Users\someone")]);

        Assert.DoesNotContain("CANARYDETAIL", text);
        Assert.DoesNotContain("someone", text);
        Assert.DoesNotContain("0a1b2c3d", text);
    }

    [Fact]
    public void AnEmptyListIsCountZeroWithNoItems()
    {
        var root = JsonNode.Parse(AttentionFile.Build(DateTimeOffset.UtcNow, []))!.AsObject();

        Assert.Equal(0, root["count"]!.GetValue<int>());
        Assert.Empty(root["items"]!.AsArray());
    }

    [Fact]
    public void TheFileIsWrittenAndDeleted()
    {
        var path = Path.Combine(_folder, AttentionFile.FileName);
        var text = AttentionFile.Build(DateTimeOffset.UtcNow, []);

        AttentionFile.Write(path, text);
        Assert.Equal(text, File.ReadAllText(path));

        AttentionFile.Delete(path);
        Assert.False(File.Exists(path));
        AttentionFile.Delete(path); // deleting a file that is not there is not an error
    }

    [Fact]
    public void TheDefaultSwitchKeyIsAttentionFile()
    {
        Assert.Equal("attentionFile", AttentionFile.Key);
        Assert.Equal("attention.json", AttentionFile.FileName);
    }
}
