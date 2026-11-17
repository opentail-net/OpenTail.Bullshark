using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Git;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// Round 7 item 4: the folder a session edits now is the latest cwd its transcript records, which can be a worktree under
/// `.claude/worktrees`. The Changes tab, the merge check and the file overlap take the session's files from that folder.
/// </summary>
public sealed class WorktreeFolderTests : IDisposable
{
    private const string Repo = @"C:\Work\repo";
    private const string Worktree = @"C:\Work\repo\.claude\worktrees\x";
    private const string SessionId = "22222222-0000-0000-0000-000000000001";
    private static readonly DateTimeOffset Start = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-worktree-" + Guid.NewGuid().ToString("N")[..8]);

    public WorktreeFolderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>A record that names the folder, as the transcript writes it (JSON text, so the backslashes are doubled).</summary>
    private static string RecordIn(string folder, string type = "user") =>
        """{"type":"__TYPE__","cwd":"__CWD__","timestamp":"2026-11-17T09:00:00Z"}""".Replace("__TYPE__", type).Replace("__CWD__", folder.Replace("\\", "\\\\"));

    private static string EditOf(string folder, string file) =>
        """{"type":"assistant","cwd":"__CWD__","timestamp":"2026-11-17T09:01:00Z","message":{"content":[{"type":"tool_use","id":"t1","name":"Edit","input":{"file_path":"__FILE__"}}]}}"""
            .Replace("__CWD__", folder.Replace("\\", "\\\\"))
            .Replace("__FILE__", file.Replace("\\", "\\\\"));

    [Fact]
    public void TheLatestCwdIsTheLastRecordThatNamesOne()
    {
        var lines = new[]
        {
            RecordIn(Repo),
            """{"type":"assistant","timestamp":"2026-11-17T09:00:30Z"}""",
            RecordIn(Worktree),
            """{"type":"user","cwd":"   ","timestamp":"2026-11-17T09:02:00Z"}""",
        };

        Assert.Equal(Worktree, TranscriptReader.Summarize(lines).LatestCwd);
    }

    [Fact]
    public void ATranscriptWithNoFolderGivesNone()
    {
        Assert.Null(TranscriptReader.Summarize(["""{"type":"user","timestamp":"2026-11-17T09:00:00Z"}"""]).LatestCwd);
    }

    [Fact]
    public void TheFactsOfATranscriptComeFromOneReadWithItsFilesAndFolder()
    {
        var projects = Path.Combine(_root, "projects");
        var file = Path.Combine(_root, "projects", "C--Work-repo", SessionId + ".jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, string.Join('\n', RecordIn(Repo), EditOf(Worktree, Path.Combine(Worktree, "a.cs"))) + "\n");

        var facts = TranscriptWrites.Facts(projects, SessionId);

        Assert.Equal(Worktree, facts.Folder);
        Assert.Contains(Path.Combine(Worktree, "a.cs"), facts.Files!);
        Assert.Null(facts.Note);
    }

    [Fact]
    public void AMissingTranscriptGivesNoFolderAndANote()
    {
        var facts = TranscriptWrites.Facts(_root, SessionId);

        Assert.Null(facts.Folder);
        Assert.Null(facts.Files);
        Assert.Equal("No transcript for this session yet.", facts.Note);
    }

    /// <summary>A folder under the test root, created: the folders below must exist to be chosen (round 8 item 6).</summary>
    private string MakeFolder(params string[] parts)
    {
        var path = Path.Combine(parts.Prepend(_root).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void TheTranscriptsFolderIsPreferredToTheListingsAndTheSpecs()
    {
        var repo = MakeFolder("repo");
        var worktree = MakeFolder("repo", ".claude", "worktrees", "x");
        var other = MakeFolder("other");

        // The worktree the transcript names is the folder read; the spelling of it does not matter (a trailing separator).
        Assert.Equal(worktree, GitChanges.FolderOf(worktree + Path.DirectorySeparatorChar, repo, repo));

        // Without one, the listing's folder, then the spec's, as before the round.
        Assert.Equal(repo, GitChanges.FolderOf(null, repo, other));
        Assert.Equal(other, GitChanges.FolderOf("  ", null, other));
        Assert.Null(GitChanges.FolderOf(null, null, null));
    }

    [Fact]
    public void AMissingTranscriptFolderFallsBackToTheListingsThenTheSpec()
    {
        // A worktree that was removed (claude rm, or merged away) is not read: the listing's folder, then the spec's, is used.
        var listed = MakeFolder("listed");
        var spec = MakeFolder("spec");
        var gone = Path.Combine(_root, "worktree-removed");

        Assert.Equal(listed, GitChanges.FolderOf(gone, listed, spec));
        Assert.Equal(spec, GitChanges.FolderOf(gone, Path.Combine(_root, "listing-gone"), spec));
        Assert.Null(GitChanges.FolderOf(gone, Path.Combine(_root, "listing-gone"), Path.Combine(_root, "spec-gone")));
    }

    [Fact]
    public void AFolderThatTheNormaliserRejectsIsSkippedAndNothingThrows()
    {
        // A NUL is not allowed in a path: the normaliser throws on it, and that candidate is skipped (round 8 item 6).
        var listed = MakeFolder("listed");

        Assert.Equal(listed, GitChanges.FolderOf("bad\0folder", listed, null));
        Assert.Null(GitChanges.FolderOf("bad\0folder", null, null));
    }

    [Fact]
    public void TheListingAndTheSpecFolderMustExistToo()
    {
        var spec = MakeFolder("spec");
        var gone = Path.Combine(_root, "gone");

        Assert.Equal(spec, GitChanges.FolderOf(gone, spec));
        Assert.Null(GitChanges.FolderOf(gone, null));
        Assert.Null(GitChanges.FolderOf(null, gone));
    }

    [Fact]
    public void AnOverlapTakesARelativePathFromTheSessionsWorktree()
    {
        // The planner writes the worktree file by its full path. The coder writes "a.cs" from its own folder, which is the worktree
        // when its transcript says so: that is the same file, and it clashes. Without the folder it is the repo's a.cs, and no clash.
        var run = Run(Repo, "run-planner", "run-coder");
        var records = Records(("run-planner", "11111111-0000-0000-0000-00000000000a"), ("run-coder", SessionId));
        var planner = new SessionWrites("11111111-0000-0000-0000-00000000000a", [Path.Combine(Worktree, "a.cs")],
            new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal) { [Path.Combine(Worktree, "a.cs")] = Start });

        var inWorktree = new SessionWrites(SessionId, ["a.cs"],
            new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal) { ["a.cs"] = Start.AddMinutes(1) }, null, Worktree);
        var clash = Assert.Single(FileOverlap.Find(run, records, [planner, inWorktree]));
        Assert.Equal(Path.Combine(Worktree, "a.cs"), clash.Path);

        var inRepo = inWorktree with { Folder = null };
        Assert.Empty(FileOverlap.Find(run, records, [planner, inRepo]));
    }

    [Fact]
    public void TheWritesCacheKeepsTheLastFolderAcrossReadsOfNewLines()
    {
        var file = Path.Combine(_root, "cache.jsonl");
        File.WriteAllText(file, RecordIn(Worktree) + "\n");
        var cache = new SessionWritesCache();

        Assert.Equal(Worktree, cache.Read(SessionId, file).Folder);

        // A line with no folder is read on its own: the folder read before is kept.
        File.AppendAllText(file, """{"type":"assistant","timestamp":"2026-11-17T09:03:00Z"}""" + "\n");
        Assert.Equal(Worktree, cache.Read(SessionId, file).Folder);

        // A later line that names another folder moves it.
        File.AppendAllText(file, RecordIn(Repo) + "\n");
        Assert.Equal(Repo, cache.Read(SessionId, file).Folder);
    }

    private static RunRecord Run(string folder, string plannerName, string coderName) =>
        new("abcd", "plan-code", folder, "auto", Start,
        [
            new SessionSpec(SessionRole.Planner, plannerName, folder, "opus", "high", "brief", "auto"),
            new SessionSpec(SessionRole.Coder, coderName, folder, "haiku", "medium", "brief", "auto"),
        ]);

    private static IReadOnlyDictionary<string, SessionRecord> Records(params (string Name, string SessionId)[] pairs) =>
        pairs.ToDictionary(
            p => p.Name,
            p => new SessionRecord(p.Name.EndsWith("planner", StringComparison.Ordinal) ? SessionRole.Planner : SessionRole.Coder,
                p.SessionId, p.Name, Start, 0, [], null, null),
            StringComparer.Ordinal);
}
