using OpenTail.Bullshark.Core.Git;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Git;

/// <summary>
/// The merge check (gems item 2): clean, conflicts parsed from `--name-only -z`, the default branch lookup, not a repository,
/// and the timeout. Git is a fake here. The output shapes were checked against git 2.53 in a scratch repository.
/// </summary>
public sealed class MergeCheckTests : IDisposable
{
    private const string Oid = "2442984316d475ef236538446948cdd904f9539d";
    private const string RemoteHead = "refs/remotes/origin/main";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "bullshark-merge-" + Guid.NewGuid().ToString("N")[..8]);

    public MergeCheckTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static GitOutput Output(int exit, string stdout, string stderr = "") =>
        new(exit, stdout, stderr, TimedOut: false, OutputTruncated: false);

    private static readonly GitOutput NoRemoteHead = Output(128, string.Empty,
        "fatal: ref refs/remotes/origin/HEAD is not a symbolic ref");

    /// <summary>
    /// A scripted repository. The remote head, the local branches that exist and the merge answer are set per test; any other
    /// command is a failure, so a test notices a command it did not expect.
    /// </summary>
    private static FakeGitRunner Repo(GitOutput? remoteHead = null, bool hasMain = false, bool hasMaster = false, GitOutput? merge = null) =>
        new(args => args[0] switch
        {
            "symbolic-ref" => remoteHead ?? Output(0, RemoteHead + "\n"),
            "rev-parse" => args[^1] == "refs/heads/main" && hasMain ? Output(0, "abc\n")
                : args[^1] == "refs/heads/master" && hasMaster ? Output(0, "abc\n")
                : Output(1, string.Empty),
            "merge-tree" => merge ?? Output(0, Oid + "\n"),
            _ => FakeGitRunner.Fail("unexpected command"),
        });

    private static IReadOnlyList<string> MergeArgs(FakeGitRunner git) =>
        git.Calls.Single(c => c.Args[0] == "merge-tree").Args;

    [Fact]
    public async Task ACleanMergeIsReportedWithItsTargetAndTheCommittedOnlyNote()
    {
        var git = Repo();

        var report = await MergeCheck.RunAsync(_folder, git);

        Assert.Equal(MergeCheckKind.Clean, report.Kind);
        Assert.Equal(RemoteHead, report.Target);
        Assert.Empty(report.Files);
        Assert.Contains("merges cleanly", report.Message);
        Assert.Contains("Checks committed work only", report.Message);
        Assert.Equal(["merge-tree", "--write-tree", "--name-only", "-z", RemoteHead, "HEAD"], MergeArgs(git));
    }

    [Fact]
    public async Task ConflictingFilesAreReadExactlyIncludingSpacesAndNonAsciiNames()
    {
        // The shape git 2.53 prints with -z: the tree id, one NUL-ended path per file, an empty field, then the messages.
        var output = Oid + "\0a.cs\0sp ace.txt\0dé.txt\0\0" +
                     "1\0a.cs\0a.cs\0a.cs\0" +
                     "CONFLICT (content): Merge conflict in a.cs\n";
        var report = await MergeCheck.RunAsync(_folder, Repo(merge: Output(1, output)));

        Assert.Equal(MergeCheckKind.Conflicts, report.Kind);
        Assert.Equal(["a.cs", "sp ace.txt", "dé.txt"], report.Files);
        Assert.Contains("in 3 files", report.Message);
        Assert.Contains("Checks committed work only", report.Message);
    }

    [Fact]
    public async Task OneConflictingFileIsSaidInTheSingular()
    {
        var output = Oid + "\0only.cs\0\0";

        var report = await MergeCheck.RunAsync(_folder, Repo(merge: Output(1, output)));

        Assert.Equal(["only.cs"], report.Files);
        Assert.Contains("in 1 file.", report.Message);
    }

    [Fact]
    public async Task WithNoOriginHeadTheLocalMainIsTheTarget()
    {
        var git = Repo(remoteHead: NoRemoteHead, hasMain: true);

        var report = await MergeCheck.RunAsync(_folder, git);

        Assert.Equal(MergeCheckKind.Clean, report.Kind);
        Assert.Equal("main", report.Target);
        Assert.Equal("main", MergeArgs(git)[^2]);
    }

    [Fact]
    public async Task WithNoOriginHeadAndNoMainTheLocalMasterIsTheTarget()
    {
        var git = Repo(remoteHead: NoRemoteHead, hasMaster: true);

        var report = await MergeCheck.RunAsync(_folder, git);

        Assert.Equal("master", report.Target);
        Assert.Equal("master", MergeArgs(git)[^2]);
    }

    [Fact]
    public async Task WithNoDefaultBranchNothingIsMergedAndTheReportSaysSo()
    {
        var git = Repo(remoteHead: NoRemoteHead);

        var report = await MergeCheck.RunAsync(_folder, git);

        Assert.Equal(MergeCheckKind.NoDefaultBranch, report.Kind);
        Assert.Empty(git.Calls.Where(c => c.Args[0] == "merge-tree"));
        Assert.Contains("No default branch", report.Message);
    }

    [Fact]
    public async Task AFolderThatIsNotARepositoryIsAPlainMessage()
    {
        var notARepo = Output(128, string.Empty, "fatal: not a git repository (or any of the parent directories): .git");

        var report = await MergeCheck.RunAsync(_folder, Repo(remoteHead: notARepo));

        Assert.Equal(MergeCheckKind.NotARepository, report.Kind);
        Assert.Contains("not in a git repository", report.Message);
    }

    [Fact]
    public async Task AMissingFolderIsAProblemAndGitIsNotRun()
    {
        var git = Repo();
        var missing = Path.Combine(_folder, "gone");

        var report = await MergeCheck.RunAsync(missing, git);

        Assert.Equal(MergeCheckKind.Problem, report.Kind);
        Assert.Empty(git.Calls);
    }

    [Fact]
    public async Task AMergeThatTakesTooLongIsAProblemNamingTheTimeLimit()
    {
        var timedOut = new GitOutput(-1, string.Empty, string.Empty, TimedOut: true, OutputTruncated: false);

        var report = await MergeCheck.RunAsync(_folder, Repo(merge: timedOut));

        Assert.Equal(MergeCheckKind.Problem, report.Kind);
        Assert.Contains("took longer than 10 seconds", report.Message);
    }

    [Fact]
    public async Task AMergeThatCannotRunIsAProblemNotAListOfConflicts()
    {
        // A missing ref: exit 1, but no tree id and no file list. That is not a conflict.
        var report = await MergeCheck.RunAsync(_folder,
            Repo(merge: Output(1, string.Empty, "merge-tree: nosuchref - not something we can merge")));

        Assert.Equal(MergeCheckKind.Problem, report.Kind);
        Assert.Empty(report.Files);
        Assert.Contains("not something we can merge", report.Message);
    }

    [Fact]
    public async Task AFileListThatWasCutShortIsNotTrusted()
    {
        // No empty field closes the list, so it was cut at the limit: the check says so rather than report a partial list.
        var cut = new GitOutput(1, Oid + "\0a.cs\0b.c", string.Empty, TimedOut: false, OutputTruncated: true);

        var report = await MergeCheck.RunAsync(_folder, Repo(merge: cut));

        Assert.Equal(MergeCheckKind.Problem, report.Kind);
    }

    [Fact]
    public void ABranchIsShownWithoutItsRefsPrefix()
    {
        Assert.Equal("origin/main", MergeCheck.DisplayName("refs/remotes/origin/main"));
        Assert.Equal("master", MergeCheck.DisplayName("refs/heads/master"));
        Assert.Equal("main", MergeCheck.DisplayName("main"));
    }

    [Fact]
    public void ConflictFilesNeedAnObjectIdFirst()
    {
        Assert.Null(MergeCheck.ConflictFiles("not-an-id\0a.cs\0\0"));
        Assert.Null(MergeCheck.ConflictFiles(string.Empty));
        Assert.Equal(["a.cs"], MergeCheck.ConflictFiles(Oid + "\0a.cs\0\0"));
        var none = MergeCheck.ConflictFiles(Oid + "\0\0");
        Assert.NotNull(none);
        Assert.Empty(none);
    }
}
