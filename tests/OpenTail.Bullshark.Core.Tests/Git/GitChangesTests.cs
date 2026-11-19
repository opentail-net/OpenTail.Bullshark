using OpenTail.Bullshark.Core.Git;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Git;

/// <summary>
/// The Changes tab's data (item 2 of the additions plan): the status and numstat parsing, the merge, not-a-repository, timeouts, the
/// 2 MB cap and the folder choice. Git itself is a fake; the real process runner is not run here.
/// </summary>
public sealed class GitChangesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "bullshark-git-" + Guid.NewGuid().ToString("N")[..8]);

    public GitChangesTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    // The -z forms, as `git status --porcelain=v1 -z` and `git diff --numstat -z` print them (checked on git 2.53, 2026-11-17).
    private const string Status = " M a b.txt\0RM moved to.cs\0new name.cs\0?? untracked.txt\0 M img.bin\0";
    private const string Numstat = "2\t1\ta b.txt\0-\t-\timg.bin\0" + "0\t0\t\0new name.cs\0moved to.cs\0";
    private const string Diff = "diff --git a/a b.txt b/a b.txt\n@@ -1,2 +1,3 @@\n one\n-two\n+TWO\n+three\n";

    [Fact]
    public void StatusKeepsSpacesAndReadsTheOldPathOfARename()
    {
        var files = GitChanges.ParseStatus(Status);

        Assert.Equal(4, files.Count);
        Assert.Equal("a b.txt", files[0].Path);
        Assert.Equal(" M", files[0].Code);
        Assert.Equal("moved to.cs", files[1].Path);
        Assert.Equal("new name.cs", files[1].OldPath);
        Assert.Equal("untracked.txt", files[2].Path);
        Assert.Equal("??", files[2].Code);
    }

    [Fact]
    public void NumstatReadsCountsBinaryFilesAndRenamesUnderTheNewPath()
    {
        var counts = GitChanges.ParseNumstat(Numstat);

        Assert.Equal(new GitChanges.NumstatCount(2, 1, Binary: false), counts["a b.txt"]);
        Assert.Equal(new GitChanges.NumstatCount(null, null, Binary: true), counts["img.bin"]);
        Assert.Equal(new GitChanges.NumstatCount(0, 0, Binary: false), counts["moved to.cs"]);
        Assert.False(counts.ContainsKey("new name.cs"));
    }

    [Fact]
    public void NumstatKeepsATabInsideAPath()
    {
        var counts = GitChanges.ParseNumstat("4\t0\tfolder\tx.txt\0");
        Assert.Equal(4, counts["folder\tx.txt"].Added);
    }

    [Theory]
    [InlineData("??", "new, not in the diff")]
    [InlineData(" M", "modified")]
    [InlineData("M ", "modified")]
    [InlineData("A ", "added")]
    [InlineData(" D", "deleted")]
    [InlineData("RM", "renamed")]
    [InlineData("UU", "conflict")]
    [InlineData(" T", "type changed")]
    [InlineData("AM", "added")]
    [InlineData("AT", "added")]
    [InlineData("AD", "deleted")]
    [InlineData("MD", "deleted")]
    [InlineData("RD", "renamed")]
    [InlineData("MM", "modified")]
    public void EachStatusCodeHasItsWords(string code, string label)
    {
        Assert.Equal(label, new GitFileChange("p", null, code, null, null, false).Label);
    }

    [Fact]
    public async Task TheListMergesTheCountsAndKeepsTheDiffAndSortsByPath()
    {
        var git = new FakeGitRunner(args => args[0] switch
        {
            "status" => FakeGitRunner.Ok(Status),
            "diff" when args[1] == "--numstat" => FakeGitRunner.Ok(Numstat),
            _ => FakeGitRunner.Ok(Diff),
        });

        var report = await GitChanges.ReadAsync(_folder, git);

        Assert.Equal(GitChangesKind.Changes, report.Kind);
        Assert.Equal(string.Empty, report.Message);
        Assert.Equal(Diff, report.Diff);
        Assert.False(report.DiffTruncated);
        Assert.Equal(["a b.txt", "img.bin", "moved to.cs", "untracked.txt"], report.Files.Select(f => f.Path).ToList());
        Assert.Equal((2, 1), (report.Files[0].Added, report.Files[0].Deleted));
        Assert.True(report.Files[1].Binary);
        Assert.Null(report.Files[3].Added);
        Assert.Equal("moved to.cs", report.Files[2].Path);
        Assert.Equal("new name.cs", report.Files[2].OldPath);
        Assert.Equal((0, 0), (report.Files[2].Added, report.Files[2].Deleted));
    }

    [Fact]
    public async Task ANoChangeFolderSaysSoInsteadOfShowingAnEmptyList()
    {
        var git = new FakeGitRunner(args => FakeGitRunner.Ok());

        var report = await GitChanges.ReadAsync(_folder, git);

        Assert.Equal(GitChangesKind.Changes, report.Kind);
        Assert.Empty(report.Files);
        Assert.StartsWith("No changes", report.Message);
    }

    [Fact]
    public async Task AFolderThatIsNotARepositoryGivesAPlainMessageAndNoFurtherCommands()
    {
        var git = new FakeGitRunner(_ => FakeGitRunner.Fail(
            "fatal: not a git repository (or any of the parent directories): .git", exitCode: 128));

        var report = await GitChanges.ReadAsync(_folder, git);

        Assert.Equal(GitChangesKind.NotARepository, report.Kind);
        Assert.Contains("not in a git repository", report.Message);
        Assert.Empty(report.Files);
        Assert.Single(git.Calls);
    }

    [Fact]
    public async Task AnyOtherFailureIsAProblemThatCarriesGitsOwnMessage()
    {
        var git = new FakeGitRunner(_ => FakeGitRunner.Fail(
            "fatal: detected dubious ownership in repository at 'C:/x'\nTo add an exception...", exitCode: 128));

        var report = await GitChanges.ReadAsync(_folder, git);

        Assert.Equal(GitChangesKind.Problem, report.Kind);
        Assert.Contains("dubious ownership", report.Message);
        Assert.Contains("exit 128", report.Message);
    }

    [Fact]
    public async Task ACommandThatRunsTooLongIsAProblemNamingTheLimit()
    {
        var git = new FakeGitRunner(_ => new GitOutput(-1, string.Empty, string.Empty, TimedOut: true, OutputTruncated: false));

        var report = await GitChanges.ReadAsync(_folder, git);

        Assert.Equal(GitChangesKind.Problem, report.Kind);
        Assert.Contains("did not finish within 10 seconds", report.Message);
    }

    [Fact]
    public async Task TheDiffCutAtTheLimitEndsWithTheTruncatedLine()
    {
        var git = new FakeGitRunner(args => args[0] switch
        {
            "status" => FakeGitRunner.Ok(" M a.cs\0"),
            "rev-parse" => FakeGitRunner.Ok("HEAD\n"),
            "diff" when args[1] == "--numstat" => FakeGitRunner.Ok("1\t0\ta.cs\0"),
            _ => new GitOutput(-1, "@@ -1 +1 @@\n+first line\n+cut here", string.Empty, TimedOut: false, OutputTruncated: true),
        });

        var report = await GitChanges.ReadAsync(_folder, git);

        Assert.True(report.DiffTruncated);
        Assert.StartsWith("@@ -1 +1 @@", report.Diff);
        Assert.EndsWith(GitChanges.DiffTruncatedNote, report.Diff);
        Assert.Equal(GitChangesKind.Changes, report.Kind);
        Assert.Single(report.Files);
    }

    [Fact]
    public async Task AListCutAtTheLimitDropsItsLastPartialEntryAndSaysSo()
    {
        var git = new FakeGitRunner(args => args[0] switch
        {
            "status" => new GitOutput(-1, " M a.cs\0 M b.c", string.Empty, TimedOut: false, OutputTruncated: true),
            _ => FakeGitRunner.Ok(),
        });

        var report = await GitChanges.ReadAsync(_folder, git);

        Assert.True(report.FilesTruncated);
        Assert.Equal(["a.cs"], report.Files.Select(f => f.Path).ToList());
        Assert.Contains("2 MB", report.Message);
    }

    [Fact]
    public async Task EveryCommandIsCappedAtTwoMegabytesAndTenSeconds()
    {
        var git = new FakeGitRunner(_ => FakeGitRunner.Ok());

        await GitChanges.ReadAsync(_folder, git);

        Assert.Equal(4, git.Calls.Count);
        foreach (var call in git.Calls)
        {
            Assert.Equal(2 * 1024 * 1024, call.MaxBytes);
            Assert.Equal(TimeSpan.FromSeconds(10), call.Timeout);
            Assert.Equal(_folder, call.Folder);
        }
        Assert.Equal(["status", "--porcelain=v1", "-z"], git.Calls[0].Args);
        Assert.Equal(["rev-parse", "--verify", "--quiet", "HEAD"], git.Calls[1].Args);
        Assert.Equal(["diff", "--numstat", "-z", "--no-textconv", "HEAD"], git.Calls[2].Args);
        Assert.Equal(["diff", "--no-color", "--no-ext-diff", "--no-textconv", "HEAD"], git.Calls[3].Args);
    }

    [Fact]
    public async Task WithAnUnbornHeadTheUnstagedAndTheStagedPartsAreReadAndAddedUp()
    {
        // No commit yet: `diff HEAD` cannot run, so the worktree part and the staged part (against the empty tree) are read apart.
        var git = new FakeGitRunner(args => args switch
        {
            ["status", ..] => FakeGitRunner.Ok(" M a.cs\0A  new.cs\0"),
            ["rev-parse", ..] => FakeGitRunner.Fail(string.Empty, exitCode: 1),
            ["diff", "--cached", "--numstat", ..] => FakeGitRunner.Ok("10\t0\ta.cs\0" + "5\t0\tnew.cs\0"),
            ["diff", "--numstat", ..] => FakeGitRunner.Ok("3\t1\ta.cs\0"),
            ["diff", "--cached", ..] => FakeGitRunner.Ok("cached diff\n"),
            ["diff", ..] => FakeGitRunner.Ok("worktree diff\n"),
            _ => FakeGitRunner.Ok(),
        });

        var report = await GitChanges.ReadAsync(_folder, git);

        Assert.Equal(GitChangesKind.Changes, report.Kind);
        Assert.Equal(["a.cs", "new.cs"], report.Files.Select(f => f.Path).ToList());
        Assert.Equal((13, 1), (report.Files[0].Added, report.Files[0].Deleted));
        Assert.Equal((5, 0), (report.Files[1].Added, report.Files[1].Deleted));
        Assert.Equal("worktree diff\ncached diff\n", report.Diff);
        Assert.False(report.DiffTruncated);
        Assert.Equal(["diff", "--numstat", "-z", "--no-textconv"], git.Calls[2].Args);
        Assert.Equal(["diff", "--cached", "--numstat", "-z", "--no-textconv"], git.Calls[3].Args);
        Assert.Equal(["diff", "--no-color", "--no-ext-diff", "--no-textconv"], git.Calls[4].Args);
        Assert.Equal(["diff", "--cached", "--no-color", "--no-ext-diff", "--no-textconv"], git.Calls[5].Args);
    }

    [Fact]
    public async Task WithAnUnbornHeadABinaryCountInEitherPartMakesTheSumBinary()
    {
        var git = new FakeGitRunner(args => args switch
        {
            ["status", ..] => FakeGitRunner.Ok("AM img.bin\0"),
            ["rev-parse", ..] => FakeGitRunner.Fail(string.Empty, exitCode: 1),
            ["diff", "--cached", "--numstat", ..] => FakeGitRunner.Ok("-\t-\timg.bin\0"),
            ["diff", "--numstat", ..] => FakeGitRunner.Ok("2\t0\timg.bin\0"),
            _ => FakeGitRunner.Ok(),
        });

        var report = await GitChanges.ReadAsync(_folder, git);

        var file = Assert.Single(report.Files);
        Assert.True(file.Binary);
        Assert.Null(file.Added);
    }

    [Fact]
    public async Task AFailedPartOfTheUnbornHeadReadIsAProblemNamingThatPart()
    {
        var git = new FakeGitRunner(args => args switch
        {
            ["status", ..] => FakeGitRunner.Ok(" M a.cs\0"),
            ["rev-parse", ..] => FakeGitRunner.Fail(string.Empty, exitCode: 1),
            ["diff", "--cached", "--numstat", ..] => FakeGitRunner.Fail("fatal: boom", exitCode: 128),
            _ => FakeGitRunner.Ok(),
        });

        var report = await GitChanges.ReadAsync(_folder, git);

        Assert.Equal(GitChangesKind.Problem, report.Kind);
        Assert.Contains("git diff --cached --numstat failed (exit 128): fatal: boom", report.Message);
    }

    [Fact]
    public async Task AFolderThatDoesNotExistIsAProblemAndNothingRuns()
    {
        var git = new FakeGitRunner(_ => FakeGitRunner.Ok());
        var missing = Path.Combine(_folder, "not-here");

        var report = await GitChanges.ReadAsync(missing, git);

        Assert.Equal(GitChangesKind.Problem, report.Kind);
        Assert.Empty(git.Calls);
    }

    [Fact]
    public void TheListedFolderWinsAndTheSpecFolderIsTheFallbackWhenItExists()
    {
        // Round 8 item 6: a folder is chosen only when it exists, so the listed and spec folders are real here.
        var listed = Path.Combine(_folder, "listed");
        var spec = Path.Combine(_folder, "spec");
        Directory.CreateDirectory(listed);
        Directory.CreateDirectory(spec);

        Assert.Equal(listed, GitChanges.FolderOf(listed, spec));
        Assert.Equal(spec, GitChanges.FolderOf(null, spec));
        Assert.Equal(spec, GitChanges.FolderOf("  ", spec));
        Assert.Equal(spec, GitChanges.FolderOf(Path.Combine(_folder, "listed-gone"), spec));
        Assert.Null(GitChanges.FolderOf(null, null));
    }
}
