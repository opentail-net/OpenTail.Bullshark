using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// Round 12 item A2: a transcript whose read fails with an error other than a cancellation is left out and listed in
/// <see cref="TranscriptIndex.Skipped"/>, and the other transcripts still load. A cancellation still stops the build.
/// </summary>
public sealed class TranscriptIndexSkipTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-index-skip-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string Assistant(string messageId, long output) =>
        "{\"type\":\"assistant\",\"timestamp\":\"2026-11-07T10:00:00.000Z\",\"requestId\":\"r-" + messageId +
        "\",\"message\":{\"id\":\"" + messageId + "\",\"model\":\"claude-opus-5-5\",\"role\":\"assistant\",\"content\":[" +
        "{\"type\":\"text\",\"text\":\"answer\"}],\"usage\":{\"input_tokens\":1,\"output_tokens\":" + output +
        ",\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":0}}}";

    /// <summary>Three transcripts, oldest first: a, b, c. Each holds one counted message with the given output.</summary>
    private string ThreeTranscripts()
    {
        var projects = Path.Combine(_root, "C--Git-skip");
        Directory.CreateDirectory(projects);
        var files = new[] { ("a", -3, 11L), ("b", -2, 22L), ("c", -1, 33L) };
        foreach (var (name, hours, output) in files)
        {
            var path = Path.Combine(projects, name + ".jsonl");
            File.WriteAllLines(path, [Assistant("m-" + name, output)]);
            File.SetCreationTimeUtc(path, DateTime.UtcNow.AddHours(hours));
        }
        return projects;
    }

    [Fact]
    public void AFileWhoseReadFailsWithAnotherErrorIsSkippedAndTheOthersLoad()
    {
        var projects = ThreeTranscripts();

        var index = TranscriptIndex.Build(projects, (path, ledger) =>
            Path.GetFileNameWithoutExtension(path) == "b"
                ? throw new InvalidOperationException("the reader cannot decode this line")
                : TranscriptReader.ReadFile(path, ledger));

        Assert.Equal(["a", "c"], index.Sessions.Select(s => s.SessionId));
        Assert.Equal(11 + 33, index.Totals.Total.Output);
        var skipped = Assert.Single(index.Skipped);
        Assert.Equal("b.jsonl", Path.GetFileName(skipped.FilePath));
        Assert.Equal("the reader cannot decode this line", skipped.Reason);
    }

    [Fact]
    public void ACancellationStillStopsTheBuild()
    {
        var projects = ThreeTranscripts();

        Assert.Throws<OperationCanceledException>(() => TranscriptIndex.Build(projects, (path, ledger) =>
            Path.GetFileNameWithoutExtension(path) == "b"
                ? throw new OperationCanceledException("cancelled")
                : TranscriptReader.ReadFile(path, ledger)));
    }
}
