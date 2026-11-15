using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

public sealed class TranscriptPagerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-pager-" + Guid.NewGuid().ToString("N")[..8]);

    public TranscriptPagerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private const string User = "{\"type\":\"user\",\"timestamp\":\"2026-11-14T09:00:00.000Z\",\"message\":{\"role\":\"user\",\"content\":\"make hello.txt\"}}";
    private const string ToolUse = "{\"type\":\"assistant\",\"timestamp\":\"2026-11-14T09:00:05.000Z\",\"message\":{\"id\":\"m1\",\"content\":[{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"Write\",\"input\":{\"file_path\":\"C:/work/hello.txt\",\"content\":\"hi\"}}]}}";
    private const string ToolResult = "{\"type\":\"user\",\"timestamp\":\"2026-11-14T09:00:06.000Z\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"t1\",\"content\":\"File created successfully\"}]}}";
    private const string Reply = "{\"type\":\"assistant\",\"timestamp\":\"2026-11-14T09:00:09.000Z\",\"message\":{\"id\":\"m2\",\"content\":[{\"type\":\"text\",\"text\":\"Done.\"}]}}";
    private const string Turn = "{\"type\":\"system\",\"subtype\":\"turn_duration\",\"durationMs\":9000,\"timestamp\":\"2026-11-14T09:00:10.000Z\"}";
    private const string Metadata = "{\"type\":\"file-history-snapshot\",\"snapshot\":{}}";

    private string Write(params string[] lines)
    {
        var path = Path.Combine(_dir, "session.jsonl");
        File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public void RecordsBecomeReadableLinesWithTheirKind()
    {
        var path = Write(User, ToolUse, ToolResult, Reply, Turn);

        var page = TranscriptPager.Open(path).ReadPage(0, 100);

        Assert.Equal(new[] { "you", "tool", "result", "claude", "turn" }, page.Select(l => l.Kind).ToArray());
        Assert.Equal("make hello.txt", page[0].Text);
        Assert.Equal("▸ Write C:/work/hello.txt", page[1].Text);
        Assert.Equal("Done.", page[3].Text);
        Assert.Equal("turn finished (9 s)", page[4].Text);
    }

    [Fact]
    public void ToolCallsAndResultsAreCollapsed()
    {
        var page = TranscriptPager.Open(Write(ToolUse, ToolResult)).ReadPage(0, 10);

        Assert.True(page[0].Collapsed);   // the tool call
        Assert.True(page[1].Collapsed);   // its result
    }

    [Fact]
    public void MetadataRecordsAreNotShown()
    {
        var page = TranscriptPager.Open(Write(Metadata, User, Metadata)).ReadPage(0, 10);

        Assert.Single(page);
    }

    [Fact]
    public void ALongResultIsCutToAPreview()
    {
        var long_ = new string('x', 500);
        var line = "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"content\":\"" + long_ + "\"}]}}";

        var shown = TranscriptPager.Open(Write(line)).ReadPage(0, 1).Single();

        Assert.True(shown.Text.Length < 200);
        Assert.EndsWith("…", shown.Text);
    }

    [Fact]
    public void PagesReadFromTheirOffsetAndStayWithinTheFile()
    {
        var lines = Enumerable.Range(1, 10).Select(i => "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"line " + i + "\"}}").ToArray();
        var pager = TranscriptPager.Open(Write(lines));

        Assert.Equal(10, pager.LineCount);
        var middle = pager.ReadPage(4, 3);
        Assert.Equal(new[] { "line 5", "line 6", "line 7" }, middle.Select(l => l.Text).ToArray());
        Assert.Equal(5, middle[0].Number);
        Assert.Equal(new[] { "line 9", "line 10" }, pager.ReadPage(8, 50).Select(l => l.Text).ToArray());
        Assert.Empty(pager.ReadPage(10, 5));
    }

    [Fact]
    public void TheViewOpensOnTheLastPage()
    {
        var lines = Enumerable.Range(1, 450).Select(i => "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"n" + i + "\"}}").ToArray();
        var pager = TranscriptPager.Open(Write(lines));

        Assert.Equal(250, pager.LastPageStart(200));
        Assert.Equal("n450", pager.ReadPage(pager.LastPageStart(200), 200).Last().Text);
    }

    [Fact]
    public void ATrailingNewlineDoesNotAddALine()
    {
        var path = Path.Combine(_dir, "trailing.jsonl");
        File.WriteAllText(path, User + "\n" + Reply + "\n");

        Assert.Equal(2, TranscriptPager.Open(path).LineCount);
    }

    [Fact]
    public void InvalidLinesAreSkipped()
    {
        var page = TranscriptPager.Open(Write("not json", User)).ReadPage(0, 10);

        Assert.Single(page);
        Assert.Equal(2, page[0].Number);
    }
}
