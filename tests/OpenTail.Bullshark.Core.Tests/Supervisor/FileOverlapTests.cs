using System.Text;
using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// Files written by more than one session of one run (gems item 1): which sessions share a file, how paths are matched, the
/// transcript writes that feed it, and the attention rule that announces a new shared file once.
/// </summary>
public sealed class FileOverlapTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    private const string PlannerName = "run-planner";
    private const string CoderName = "run-coder-1";
    private const string OtherCoderName = "run-coder-2";
    private const string PlannerId = "11111111-0000-0000-0000-000000000001";
    private const string CoderId = "11111111-0000-0000-0000-000000000002";
    private const string OtherCoderId = "11111111-0000-0000-0000-000000000003";
    private const string OutsiderId = "99999999-0000-0000-0000-000000000009";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-overlap-" + Guid.NewGuid().ToString("N")[..8]);

    public FileOverlapTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static RunRecord Run(string folder, params string[] names) =>
        new("abcd", "plan-code", folder, "auto", Start,
            names.Select((name, i) => new SessionSpec(i == 0 ? SessionRole.Planner : SessionRole.Coder, name, folder,
                "opus", "high", "brief", "auto")).ToList());

    private static SessionRecord Record(string name, string sessionId) =>
        new(name == PlannerName ? SessionRole.Planner : SessionRole.Coder, sessionId, name, Start, 0, [], null, null);

    private static IReadOnlyDictionary<string, SessionRecord> Records(params (string Name, string SessionId)[] pairs) =>
        pairs.ToDictionary(p => p.Name, p => Record(p.Name, p.SessionId), StringComparer.Ordinal);

    private static SessionWrites Writes(string sessionId, IReadOnlyList<string> files, params (string Path, int Minute)[] times) =>
        new(sessionId, files, times.ToDictionary(t => t.Path, t => Start.AddMinutes(t.Minute), StringComparer.Ordinal));

    private const string IdC = "11111111-0000-0000-0000-000000000004";
    private const string IdD = "11111111-0000-0000-0000-000000000005";
    private const string IdE = "11111111-0000-0000-0000-000000000006";
    private const string IdF = "11111111-0000-0000-0000-000000000007";

    private static AgentInfo Listed(string sessionId, string state) =>
        new(Id: sessionId[..8], SessionId: sessionId, Name: "x", Kind: "background", State: state, Status: null,
            Cwd: null, Pid: null, StartedAt: null);

    [Fact]
    public void OnlyLiveSessionsTakePartInAClash()
    {
        // Six sessions of one run. Only the working and the blocked ones are live: a done, stopped or failed session, and one the
        // listing does not show, are not (round 5 item 7). So a file shared with a finished session is not a clash any more.
        var run = Run(_root, "run-a", "run-b", "run-c", "run-d", "run-e", "run-f");
        var records = Records(("run-a", CoderId), ("run-b", OtherCoderId), ("run-c", IdC), ("run-d", IdD), ("run-e", IdE), ("run-f", IdF));
        var agents = new[] { Listed(CoderId, "working"), Listed(OtherCoderId, "blocked"), Listed(IdC, "done"), Listed(IdD, "stopped"), Listed(IdE, "failed") };

        var live = FileOverlap.LiveRecords(records, agents);

        Assert.Equal(["run-a", "run-b"], live.Keys.Order());

        var file = Path.Combine(_root, "src", "Shared.cs");
        var withFinished = FileOverlap.Find(run, live, [Writes(CoderId, [file], (file, 3)), Writes(IdC, [file], (file, 4))]);
        Assert.Empty(withFinished);

        var bothLive = FileOverlap.Find(run, live, [Writes(CoderId, [file], (file, 3)), Writes(OtherCoderId, [file], (file, 4))]);
        Assert.Equal(["run-a", "run-b"], Assert.Single(bothLive).Sessions.Select(s => s.Name));
    }

    [Fact]
    public void TwoCodersWritingOneFileIsOneClashWithBothSessionsAndTheirLastWrites()
    {
        var file = Path.Combine(_root, "src", "App.cs");
        var run = Run(_root, CoderName, OtherCoderName);
        var records = Records((CoderName, CoderId), (OtherCoderName, OtherCoderId));

        var clashes = FileOverlap.Find(run, records, [
            Writes(CoderId, [file], (file, 5)),
            Writes(OtherCoderId, [file], (file, 12)),
        ]);

        var clash = Assert.Single(clashes);
        Assert.Equal(file, clash.Path);
        Assert.Equal([CoderName, OtherCoderName], clash.Sessions.Select(s => s.Name));
        Assert.Equal(Start.AddMinutes(5), clash.Sessions[0].LastWrite);
        Assert.Equal(Start.AddMinutes(12), clash.Sessions[1].LastWrite);
    }

    [Fact]
    public void ThePlannerEditingACodersFileIsAClashToo()
    {
        var file = Path.Combine(_root, "PLAN.md");
        var run = Run(_root, PlannerName, CoderName);

        var clash = Assert.Single(FileOverlap.Find(run,
            Records((PlannerName, PlannerId), (CoderName, CoderId)),
            [Writes(PlannerId, [file], (file, 1)), Writes(CoderId, [file], (file, 2))]));

        // Sessions are listed by name, so the coder comes before the planner.
        Assert.Equal([CoderName, PlannerName], clash.Sessions.Select(s => s.Name));
    }

    [Fact]
    public void OneSessionWritingAFileTwiceIsNotAClash()
    {
        var file = Path.Combine(_root, "a.cs");
        var run = Run(_root, CoderName, OtherCoderName);

        var clashes = FileOverlap.Find(run,
            Records((CoderName, CoderId), (OtherCoderName, OtherCoderId)),
            [Writes(CoderId, [file, file], (file, 3)), Writes(OtherCoderId, [Path.Combine(_root, "b.cs")], (Path.Combine(_root, "b.cs"), 4))]);

        Assert.Empty(clashes);
    }

    [Fact]
    public void ASessionOutsideTheRunIsIgnored()
    {
        var file = Path.Combine(_root, "shared.cs");
        var run = Run(_root, CoderName);

        // The outsider has a transcript that writes the file, but no record under this run's spec names.
        var clashes = FileOverlap.Find(run,
            Records((CoderName, CoderId)),
            [Writes(CoderId, [file], (file, 1)), Writes(OutsiderId, [file], (file, 2))]);

        Assert.Empty(clashes);
    }

    [Fact]
    public void ASpecWithNoSessionIdOrNoTranscriptContributesNothing()
    {
        var file = Path.Combine(_root, "shared.cs");
        var run = Run(_root, CoderName, OtherCoderName);
        var records = new Dictionary<string, SessionRecord>(StringComparer.Ordinal)
        {
            [CoderName] = Record(CoderName, CoderId),
            [OtherCoderName] = Record(OtherCoderName, string.Empty),   // a start that failed
        };

        Assert.Empty(FileOverlap.Find(run, records, [Writes(CoderId, [file], (file, 1))]));
    }

    [Fact]
    public void ARelativePathIsTakenFromTheRunsFolderSoItMatchesTheAbsoluteOne()
    {
        var absolute = Path.Combine(_root, "src", "Core.cs");
        var run = Run(_root, CoderName, OtherCoderName);

        var clash = Assert.Single(FileOverlap.Find(run,
            Records((CoderName, CoderId), (OtherCoderName, OtherCoderId)),
            [Writes(CoderId, [Path.Combine("src", "Core.cs")], (Path.Combine("src", "Core.cs"), 1)),
             Writes(OtherCoderId, [absolute], (absolute, 2))]));

        Assert.Equal(Path.GetFullPath(absolute), clash.Path);
    }

    [Fact]
    public void PathsDifferingInCaseAndSlashesAreOneFileOnWindowsAndTwoElsewhere()
    {
        var first = Path.Combine(_root, "src", "App.cs");
        var second = first.ToUpperInvariant().Replace('\\', '/');
        var run = Run(_root, CoderName, OtherCoderName);

        var clashes = FileOverlap.Find(run,
            Records((CoderName, CoderId), (OtherCoderName, OtherCoderId)),
            [Writes(CoderId, [first], (first, 1)), Writes(OtherCoderId, [second], (second, 2))]);

        // On Windows the two spellings are one file. Elsewhere a case difference is a different file, so there is no clash.
        Assert.Equal(OperatingSystem.IsWindows() ? 1 : 0, clashes.Count);
    }

    [Fact]
    public void DifferentFilesGiveNoClash()
    {
        var run = Run(_root, CoderName, OtherCoderName);

        Assert.Empty(FileOverlap.Find(run,
            Records((CoderName, CoderId), (OtherCoderName, OtherCoderId)),
            [Writes(CoderId, [Path.Combine(_root, "one.cs")], (Path.Combine(_root, "one.cs"), 1)),
             Writes(OtherCoderId, [Path.Combine(_root, "two.cs")], (Path.Combine(_root, "two.cs"), 2))]));
    }

    [Fact]
    public void TheDetailNamesTheFileAndTheOtherSessionsWithTheirLastWrite()
    {
        var file = Path.Combine(_root, "App.cs");
        var clash = Assert.Single(FileOverlap.Find(Run(_root, CoderName, OtherCoderName),
            Records((CoderName, CoderId), (OtherCoderName, OtherCoderId)),
            [Writes(CoderId, [file], (file, 5)), Writes(OtherCoderId, [file], (file, 12))]));

        var detail = FileOverlap.Detail(clash, CoderName);

        Assert.Contains(Path.GetFullPath(file), detail);
        Assert.Contains($"{OtherCoderName} (last write {Start.AddMinutes(12).ToLocalTime():HH:mm})", detail);
        Assert.DoesNotContain(CoderName + " (last", detail);
    }

    [Fact]
    public void ReadsTheWritesOfATranscriptAndReadsItAgainOnlyWhenItChanged()
    {
        var transcript = Path.Combine(_root, CoderId + ".jsonl");
        File.WriteAllLines(transcript, [Assistant("t1", "2026-11-17T09:05:00Z", Path.Combine(_root, "a.cs"))]);
        var cache = new SessionWritesCache();

        var first = cache.Read(CoderId, transcript);
        Assert.Equal(Start.AddMinutes(5), first.LastWrites[Path.Combine(_root, "a.cs")]);

        // Unchanged: the same answer comes back, with no second read.
        Assert.Same(first, cache.Read(CoderId, transcript));

        // Appended to: the new write is seen, and a later write to the same file moves its last-write time.
        File.AppendAllLines(transcript, [Assistant("t2", "2026-11-17T09:20:00Z", Path.Combine(_root, "a.cs"))]);
        var second = cache.Read(CoderId, transcript);
        Assert.Single(second.Files);
        Assert.Equal(Start.AddMinutes(20), second.LastWrites[Path.Combine(_root, "a.cs")]);
    }

    /// <summary>A Write record that also carries the message's usage, so the token totals can be compared.</summary>
    private static string Usage(string id, string timestamp, string path, long output) =>
        JsonSerializer.Serialize(new
        {
            type = "assistant",
            timestamp,
            requestId = "r-" + id,
            message = new
            {
                id = "m-" + id,
                model = "claude-opus-5-5",
                content = new object[] { new { type = "tool_use", id, name = "Write", input = new { file_path = path, content = "x" } } },
                usage = new { input_tokens = 3L, output_tokens = output, cache_creation_input_tokens = 0L, cache_read_input_tokens = 0L },
            },
        });

    /// <summary>A stream that counts the bytes read from it, so a test can see how much of a transcript was read.</summary>
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    [Fact]
    public void AGrownTranscriptReadsOnlyTheAppendedBytesAndGivesTheSameAnswerAsAFullRead()
    {
        var transcript = Path.Combine(_root, CoderId + ".jsonl");
        var first = Path.Combine(_root, "a.cs");
        var second = Path.Combine(_root, "b.cs");
        File.WriteAllText(transcript, Usage("u1", "2026-11-17T09:05:00Z", first, 100) + "\n");
        var opened = new List<CountingStream>();
        var cache = new SessionWritesCache(path =>
        {
            var stream = new CountingStream(TranscriptFile.Open(path));
            opened.Add(stream);
            return stream;
        });
        cache.Read(CoderId, transcript);
        Assert.Equal(new FileInfo(transcript).Length, opened[0].BytesRead);

        var appended = Usage("u2", "2026-11-17T09:20:00Z", second, 40);
        File.AppendAllText(transcript, appended + "\n");
        var grown = cache.Read(CoderId, transcript);

        // Only the appended line was read, and the answer is what a full read of the file gives.
        Assert.Equal(Encoding.UTF8.GetByteCount(appended) + 1, opened[1].BytesRead);
        var whole = TranscriptReader.ReadFile(transcript);
        Assert.Equal(whole.FilesWritten, grown.Files);
        Assert.Equal(whole.TotalUsage, grown.Usage);
        Assert.Equal(whole.FileLastWrites!.Count, grown.LastWrites.Count);
        foreach (var (file, when) in whole.FileLastWrites!) Assert.Equal(when, grown.LastWrites[file]);
    }

    [Fact]
    public void AnUnchangedTranscriptIsNotOpenedAgain()
    {
        var transcript = Path.Combine(_root, CoderId + ".jsonl");
        File.WriteAllText(transcript, Usage("u1", "2026-11-17T09:05:00Z", Path.Combine(_root, "a.cs"), 100) + "\n");
        var opens = 0;
        var cache = new SessionWritesCache(path => { opens++; return TranscriptFile.Open(path); });

        cache.Read(CoderId, transcript);
        cache.Read(CoderId, transcript);
        cache.Read(CoderId, transcript);

        Assert.Equal(1, opens);
    }

    [Fact]
    public void ALineStillBeingWrittenCountsOnlyWhenItsNewlineIsThere()
    {
        var transcript = Path.Combine(_root, CoderId + ".jsonl");
        var first = Path.Combine(_root, "a.cs");
        var second = Path.Combine(_root, "b.cs");
        var secondLine = Assistant("t2", "2026-11-17T09:20:00Z", second);
        File.WriteAllText(transcript, Assistant("t1", "2026-11-17T09:05:00Z", first) + "\n" + secondLine[..40]);
        var cache = new SessionWritesCache();

        // The second record is cut off: it is not read yet.
        Assert.Equal([first], cache.Read(CoderId, transcript).Files);

        File.AppendAllText(transcript, secondLine[40..] + "\n");
        Assert.Equal([first, second], cache.Read(CoderId, transcript).Files);
    }

    [Fact]
    public void ATranscriptRewrittenShorterIsReadFromItsStart()
    {
        var transcript = Path.Combine(_root, CoderId + ".jsonl");
        var first = Path.Combine(_root, "a.cs");
        var second = Path.Combine(_root, "b.cs");
        var replacement = Path.Combine(_root, "c.cs");
        File.WriteAllLines(transcript, [Assistant("t1", "2026-11-17T09:05:00Z", first), Assistant("t2", "2026-11-17T09:06:00Z", second)]);
        var cache = new SessionWritesCache();
        Assert.Equal([first, second], cache.Read(CoderId, transcript).Files);

        // Nothing of the old file is kept: the new one is read from its first byte.
        File.WriteAllLines(transcript, [Assistant("t3", "2026-11-17T09:30:00Z", replacement)]);
        Assert.Equal([replacement], cache.Read(CoderId, transcript).Files);
    }

    [Fact]
    public void TheByteOrderMarkOfATranscriptDoesNotHideItsFirstRecord()
    {
        var transcript = Path.Combine(_root, CoderId + ".jsonl");
        var first = Path.Combine(_root, "a.cs");
        File.WriteAllText(transcript, Assistant("t1", "2026-11-17T09:05:00Z", first) + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.Equal([first], new SessionWritesCache().Read(CoderId, transcript).Files);
    }

    [Fact]
    public void ARunWithOneSessionAndNoLimitHasNoTranscriptToRead()
    {
        var single = Run(_root, CoderName);

        Assert.False(FileOverlap.NeedsTranscripts(single));
        Assert.True(FileOverlap.NeedsTranscripts(Run(_root, CoderName, OtherCoderName)));
        Assert.True(FileOverlap.NeedsTranscripts(single with { Limits = new RunLimits(1000, null) }));
    }

    /// <summary>One assistant record with a Write tool call, written as JSON so the path is escaped properly.</summary>
    private static string Assistant(string id, string timestamp, string path) =>
        JsonSerializer.Serialize(new
        {
            type = "assistant",
            timestamp,
            requestId = "r-" + id,
            message = new
            {
                id = "m-" + id,
                model = "claude-opus-5-5",
                content = new object[] { new { type = "tool_use", id, name = "Write", input = new { file_path = path, content = "x" } } },
            },
        });
}
