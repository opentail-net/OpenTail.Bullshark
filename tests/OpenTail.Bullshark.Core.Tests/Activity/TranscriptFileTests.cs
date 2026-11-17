using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// A transcript that a live session still holds open for writing can be read and paged by OpenTail.Bullshark, and reading it
/// does not stop the session from writing.
/// </summary>
public sealed class TranscriptFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-tfile-" + Guid.NewGuid().ToString("N")[..8]);

    public TranscriptFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private const string User = "{\"type\":\"user\",\"timestamp\":\"2026-11-17T09:00:00.000Z\",\"message\":{\"role\":\"user\",\"content\":\"first question\"}}";
    private const string Reply = "{\"type\":\"assistant\",\"timestamp\":\"2026-11-17T09:00:05.000Z\",\"message\":{\"id\":\"m1\",\"content\":[{\"type\":\"text\",\"text\":\"first answer\"}]}}";
    private const string Turn = "{\"type\":\"system\",\"subtype\":\"turn_duration\",\"durationMs\":5000,\"timestamp\":\"2026-11-17T09:00:06.000Z\"}";

    [Fact]
    public void AFileHeldForWritingCanBeReadAndPaged()
    {
        var path = Path.Combine(_dir, "session.jsonl");
        File.WriteAllLines(path, [User, Reply, Turn]);

        // The live session holds the file for writing, and shares it for reading, writing and deleting, as Claude does.
        using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        Assert.Equal(new[] { User, Reply, Turn }, TranscriptFile.ReadLines(path).ToArray());
        var page = TranscriptPager.Open(path).ReadPage(0, 10);
        Assert.Equal(new[] { "you", "claude", "turn" }, page.Select(l => l.Kind).ToArray());
        Assert.Equal(1, TranscriptReader.ReadFile(path).UserMessages);
        Assert.Single(TurnFacts.ReadFile(path));
    }

    [Fact]
    public void AReaderDoesNotStopTheSessionFromWriting()
    {
        var path = Path.Combine(_dir, "session.jsonl");
        File.WriteAllLines(path, [User]);

        using var reader = TranscriptFile.Open(path);
        using (var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(Reply + Environment.NewLine);
            writer.Write(bytes, 0, bytes.Length);
        }

        Assert.Equal(2, File.ReadAllLines(path).Length);
    }
}
