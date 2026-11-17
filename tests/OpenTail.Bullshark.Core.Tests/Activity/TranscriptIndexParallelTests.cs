using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// The default index build reads the transcripts on several threads (round 10 item 2). It must give what the build that reads them one
/// after another gives. The fixture is one where the reading order matters: messages copied between files, one message written twice
/// with different counts, one message under two models, messages with no key, a subagent file, files created out of name order, a line
/// that is not JSON, and a file that cannot be opened.
/// </summary>
public sealed class TranscriptIndexParallelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-parallel-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Project(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void TheDefaultBuildGivesTheSameIndexAsReadingTheFilesOneAfterAnother()
    {
        var created = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
        var models = new[] { "claude-opus-5-5", "claude-haiku-5-5" };
        var folders = new[] { Project("C--Git-alpha"), Project("C--Git-beta"), Project("C--Git-gamma") };

        // Twelve transcripts. File i is created on day (i * 5) % 12, so the reading order is not the name order. Each copies some of a
        // pool of six messages, with a count and a model that differ from file to file.
        for (var i = 0; i < 12; i++)
        {
            var stamp = "2026-11-01T10:" + i.ToString("D2") + ":00.000Z";
            var lines = new List<string>();
            for (var k = 0; k < 6; k++)
            {
                if ((i + k) % 2 == 1) continue;
                lines.Add(IndexFixture.Assistant("pool" + k, "req" + k, 10 + (i * 7 + k * 3) % 50, stamp, models[(i + k) % 2]));
            }
            // A message written twice in one file, the second with the larger count (the placeholder rule).
            lines.Add(IndexFixture.Assistant("dup" + i, "rdup" + i, 4, stamp, models[0]));
            lines.Add(IndexFixture.Assistant("dup" + i, "rdup" + i, 300 + i, stamp, models[0]));
            // A message with no key, counted where it stands.
            lines.Add("{\"type\":\"assistant\",\"timestamp\":\"" + stamp + "\",\"message\":{\"id\":\"nokey" + i + "\",\"model\":\"" +
                      models[1] + "\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"no key\"}],\"usage\":{" +
                      "\"input_tokens\":1,\"output_tokens\":" + (5 + i) + ",\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":0}}}");
            if (i == 4) lines.Add("not json at all");

            var path = Path.Combine(folders[i % 3], $"aaaaaaaa-0000-0000-0000-{i:D12}.jsonl");
            File.WriteAllLines(path, lines);
            File.SetCreationTimeUtc(path, created.AddDays(i * 5 % 12));
        }

        // A subagent transcript, which keeps its own digest.
        var subagents = Path.Combine(folders[0], "subagents");
        Directory.CreateDirectory(subagents);
        var agent = Path.Combine(subagents, "agent-1.jsonl");
        File.WriteAllLines(agent, [IndexFixture.Assistant("pool1", "req1", 77, "2026-11-02T10:00:00.000Z", models[0])]);
        File.SetCreationTimeUtc(agent, created.AddDays(20));

        // A transcript that is held open with no sharing, so it cannot be read by either build.
        var locked = Path.Combine(Project("C--Git-locked"), "bbbbbbbb-0000-0000-0000-000000000099.jsonl");
        File.WriteAllLines(locked, [IndexFixture.Assistant("pool0", "req0", 1000, "2026-11-03T10:00:00.000Z", models[0])]);
        File.SetCreationTimeUtc(locked, created.AddDays(15));
        using var held = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var oneAfterAnother = TranscriptIndex.Build(_root, TranscriptReader.ReadFile);
        var parallel = TranscriptIndex.Build(_root);

        Assert.Equal(IndexFixture.Describe(oneAfterAnother), IndexFixture.Describe(parallel));
        Assert.Equal(13, parallel.Sessions.Count);
        Assert.Single(parallel.Skipped);
        // Six pool messages, twelve messages written twice (once each), and twelve messages with no key.
        Assert.Equal(30, parallel.Totals.CountedMessages);
    }

    [Fact]
    public void AMissingOrEmptyFolderGivesTheSameIndexEitherWay()
    {
        var empty = Project("C--Git-empty");

        foreach (var root in new[] { Path.Combine(_root, "absent"), empty })
        {
            var oneAfterAnother = TranscriptIndex.Build(root, TranscriptReader.ReadFile);
            var parallel = TranscriptIndex.Build(root);
            Assert.Equal(IndexFixture.Describe(oneAfterAnother), IndexFixture.Describe(parallel));
            Assert.Empty(parallel.Sessions);
            Assert.Equal(0, parallel.Totals.CountedMessages);
        }
    }
}
