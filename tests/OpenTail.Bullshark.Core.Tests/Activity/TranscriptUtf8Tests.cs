using System.Text;
using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// Round 12 item A6(b): the index parses each line as UTF-8 bytes. It must give the index the text reader gives, for line breaks of every kind,
/// a byte order mark (at the start and mid-file), invalid UTF-8 inside a string, non-ASCII text and an unreadable line. The index is compared
/// with the one-after-another build that reads the text, which is the reference the bench's verify step uses.
/// </summary>
public sealed class TranscriptUtf8Tests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-utf8-" + Guid.NewGuid().ToString("N")[..8]);

    public TranscriptUtf8Tests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string Assistant(string id, string text, long output = 5) =>
        "{\"type\":\"assistant\",\"timestamp\":\"2026-11-17T10:00:05Z\",\"requestId\":\"req-" + id + "\",\"message\":{\"id\":\"" + id +
        "\",\"model\":\"claude-opus-5-5\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"" + text + "\"}]," +
        "\"usage\":{\"input_tokens\":3,\"output_tokens\":" + output + ",\"cache_creation_input_tokens\":2,\"cache_read_input_tokens\":100}}}";

    private static string User(string text) =>
        "{\"type\":\"user\",\"timestamp\":\"2026-11-17T10:00:00Z\",\"message\":{\"role\":\"user\",\"content\":\"" + text + "\"}}";

    /// <summary>A line whose text holds one raw byte that is not valid UTF-8 (the reader sees U+FFFD for it).</summary>
    private static byte[] WithRawByte(string before, byte raw, string after) =>
        [.. Encoding.UTF8.GetBytes(before), raw, .. Encoding.UTF8.GetBytes(after)];

    private string Folder(string name)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string Describe(TranscriptIndex index) => IndexFixture.Describe(index);

    [Fact]
    public void TheIndexOverTheUtf8ReaderIsTheSameAsOverTheTextReader()
    {
        var folder = Folder("mixed");

        // Byte order mark, CRLF, a blank line, a lone carriage return, invalid JSON, accented and emoji text, and no newline at the end.
        var first = Encoding.UTF8.GetBytes("﻿" + User("bom first") + "\r\n\n" + "{ not json\r" + Assistant("m1", "café 😀 text") + "\n\r\n" + User("last without a newline"));
        File.WriteAllBytes(Path.Combine(folder, "first.jsonl"), first);

        // Invalid UTF-8 inside a string, a line with a lone surrogate escape (unreadable, skipped), and a byte order mark mid-file.
        var second = new List<byte>();
        second.AddRange(WithRawByte("{\"type\":\"assistant\",\"timestamp\":\"2026-11-17T10:00:06Z\",\"requestId\":\"req-raw\",\"message\":{\"id\":\"raw\"," +
            "\"model\":\"claude-opus-5-5\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"bad ", 0xFF,
            "\"}],\"usage\":{\"input_tokens\":1,\"output_tokens\":7,\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":0}}}\n"));
        second.AddRange(Encoding.UTF8.GetBytes(Assistant("bad", "broken \\ud800 text") + "\n"));
        second.AddRange(Encoding.UTF8.GetBytes(User("line one") + "\n" + "﻿" + Assistant("m9", "after a mark", output: 9) + "\n"));
        File.WriteAllBytes(Path.Combine(folder, "second.jsonl"), second.ToArray());

        var viaBytes = TranscriptIndex.Build(_root);
        var viaText = TranscriptIndex.Build(_root, TranscriptReader.ReadFile);

        Assert.Equal(Describe(viaText), Describe(viaBytes));
        Assert.Equal(2, viaBytes.Sessions.Count);
        // The unreadable line costs its own line only: both files load.
        Assert.Empty(viaBytes.Skipped);
    }

    /// <summary>A user record padded with ASCII to exactly <paramref name="bytes"/> bytes (the line, without its break).</summary>
    private static string Padded(int bytes) => User(new string('p', bytes - User(string.Empty).Length));

    [Fact]
    public void ACarriageReturnAtTheEndOfAReadChunkAndItsLineFeedAtTheStartOfTheNextIsOneBreak()
    {
        // The byte reader reads 256 KB at a time. The first line ends at the last byte of the first read, the carriage return; its line
        // feed opens the next read, and it must not start an empty line.
        const int chunk = 256 * 1024;
        var folder = Folder("chunks");
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.UTF8.GetBytes(Padded(chunk - 1)));
        bytes.Add((byte)'\r');
        bytes.Add((byte)'\n');
        bytes.AddRange(Encoding.UTF8.GetBytes(Assistant("after", "after the break") + "\r\n" + User("last") + "\n"));
        File.WriteAllBytes(Path.Combine(folder, "chunks.jsonl"), bytes.ToArray());

        var viaBytes = TranscriptIndex.Build(_root);
        var viaText = TranscriptIndex.Build(_root, TranscriptReader.ReadFile);

        Assert.Equal(Describe(viaText), Describe(viaBytes));
        var session = Assert.Single(viaBytes.Sessions);
        Assert.Equal(3, session.Turns + session.UserMessages + session.AssistantMessages);
        Assert.Equal(2, session.UserMessages);
    }

    [Fact]
    public void AnInvalidUtf8LineIsReadAsTheTextReaderReadsIt()
    {
        var folder = Folder("invalid");
        File.WriteAllBytes(Path.Combine(folder, "invalid.jsonl"), WithRawByte(
            "{\"type\":\"assistant\",\"timestamp\":\"2026-11-17T10:00:06Z\",\"requestId\":\"req-x\",\"message\":{\"id\":\"x\"," +
            "\"model\":\"claude-opus-5-5\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"a ", 0xFE,
            " b\"}],\"usage\":{\"input_tokens\":2,\"output_tokens\":4,\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":0}}}"));

        var index = TranscriptIndex.Build(_root);

        // The line is counted, with its text as the text reader decodes it: the byte above 0x7F becomes U+FFFD.
        var session = Assert.Single(index.Sessions);
        Assert.Equal(1, session.AssistantMessages);
        Assert.Equal("a � b", session.LastAssistantText);
        Assert.Equal(4, index.Totals.Total.Output);
        Assert.Equal(Describe(TranscriptIndex.Build(_root, TranscriptReader.ReadFile)), Describe(index));
    }
}
