using System.Diagnostics;
using OpenTail.Bullshark.Core.Git;
using OpenTail.Bullshark.Core.Library;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Git;

/// <summary>
/// The real `git` process, on scratch repositories: the same reads as the fake tests, against what git prints. Needs `git` on the PATH.
/// On a machine without it the tests return at once, so they check nothing there (the fake-based tests still run).
/// </summary>
public sealed class ProcessGitRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-gitproc-" + Guid.NewGuid().ToString("N")[..8]);

    public ProcessGitRunnerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        // git writes its object files read-only; they are made writable so the scratch folder can go.
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch (IOException) { }
        }
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static bool GitInstalled()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "--version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            process?.WaitForExit(10_000);
            return process is { ExitCode: 0 };
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Runs git for the set-up of a scratch repository (not the code under test).</summary>
    private static void Git(string folder, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(folder);
        foreach (var argument in new[] { "-c", "user.name=test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false" }
                     .Concat(arguments)) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private string NewRepository(string name)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        Git(folder, "init", "-q");
        File.WriteAllText(Path.Combine(folder, "a b.txt"), "one\ntwo\n");
        Git(folder, "add", ".");
        Git(folder, "commit", "-q", "-m", "first");
        return folder;
    }

    [Fact]
    public async Task TheRealGitCountsAChangedFileWithAnySpaceInItsName()
    {
        if (!GitInstalled()) return;
        var repo = NewRepository("repo");
        File.WriteAllText(Path.Combine(repo, "a b.txt"), "one\nTWO\nthree\n");
        File.WriteAllText(Path.Combine(repo, "new file.txt"), "fresh\n");

        var report = await GitChanges.ReadAsync(repo, new ProcessGitRunner());

        Assert.Equal(GitChangesKind.Changes, report.Kind);
        var changed = Assert.Single(report.Files, f => f.Path == "a b.txt");
        Assert.Equal((2, 1), (changed.Added, changed.Deleted));
        var fresh = Assert.Single(report.Files, f => f.Path == "new file.txt");
        Assert.Null(fresh.Added);
        Assert.Contains("+three", report.Diff);
    }

    [Fact]
    public async Task TheRealGitSaysSoForAFolderThatIsNotARepository()
    {
        if (!GitInstalled()) return;
        var plain = Path.Combine(_root, "plain");
        Directory.CreateDirectory(plain);

        var report = await GitChanges.ReadAsync(plain, new ProcessGitRunner());

        Assert.Equal(GitChangesKind.NotARepository, report.Kind);
    }

    [Fact]
    public async Task ADiffLongerThanTheLimitIsStoppedAndSaidSo()
    {
        if (!GitInstalled()) return;
        var repo = NewRepository("big");
        // Well over 2 MB of added lines: the process is stopped at the limit, not read to the end.
        var line = new string('x', 99) + "\n";
        File.WriteAllText(Path.Combine(repo, "a b.txt"), string.Concat(Enumerable.Repeat(line, 30_000)));

        var report = await GitChanges.ReadAsync(repo, new ProcessGitRunner());

        Assert.True(report.DiffTruncated);
        Assert.EndsWith(GitChanges.DiffTruncatedNote, report.Diff);
        Assert.True(report.Diff.Length <= GitChanges.MaxOutputBytes + 200);
    }

    private static string NewRepositoryWithoutCommit(string folder)
    {
        Directory.CreateDirectory(folder);
        Git(folder, "init", "-q");
        return folder;
    }

    [Fact]
    public async Task AStagedChangeShowsItsHunksAndCountsInTheListAndTheDiff()
    {
        if (!GitInstalled()) return;
        var repo = NewRepository("staged");
        File.WriteAllText(Path.Combine(repo, "a b.txt"), "one\nTWO\n");
        Git(repo, "add", "a b.txt");

        var report = await GitChanges.ReadAsync(repo, new ProcessGitRunner());

        var file = Assert.Single(report.Files, f => f.Path == "a b.txt");
        Assert.Equal((1, 1), (file.Added, file.Deleted));
        Assert.Contains("+TWO", report.Diff);
        Assert.NotNull(ViewedFiles.CurrentStamp(repo, "a b.txt"));
    }

    [Fact]
    public async Task AStagedAndAFurtherUnstagedChangeShowBothSetsOfHunks()
    {
        if (!GitInstalled()) return;
        var repo = NewRepository("both");
        File.WriteAllText(Path.Combine(repo, "a b.txt"), "one\nTWO\n");
        Git(repo, "add", "a b.txt");
        File.WriteAllText(Path.Combine(repo, "a b.txt"), "one\nTWO\nthree\n");

        var report = await GitChanges.ReadAsync(repo, new ProcessGitRunner());

        // Against HEAD: "two" is gone, "TWO" and "three" are new.
        var file = Assert.Single(report.Files, f => f.Path == "a b.txt");
        Assert.Equal((2, 1), (file.Added, file.Deleted));
        Assert.Contains("+TWO", report.Diff);
        Assert.Contains("+three", report.Diff);
    }

    [Fact]
    public async Task ARenameStagedInTheIndexIsListedAsARenameWithItsOldPath()
    {
        if (!GitInstalled()) return;
        var repo = NewRepository("renamed");
        Git(repo, "mv", "a b.txt", "moved.txt");

        var report = await GitChanges.ReadAsync(repo, new ProcessGitRunner());

        var file = Assert.Single(report.Files);
        Assert.Equal("moved.txt", file.Path);
        Assert.Equal("a b.txt", file.OldPath);
        Assert.Equal("renamed", file.Label);
        Assert.Equal((0, 0), (file.Added, file.Deleted));
    }

    [Fact]
    public async Task AFolderWithoutAnyCommitListsItsStagedAndUntrackedFiles()
    {
        if (!GitInstalled()) return;
        var repo = NewRepositoryWithoutCommit(Path.Combine(_root, "unborn"));
        File.WriteAllText(Path.Combine(repo, "staged.txt"), "first\nsecond\n");
        File.WriteAllBytes(Path.Combine(repo, "img.bin"), [0, 1, 2, 3]);
        Git(repo, "add", "staged.txt", "img.bin");
        File.WriteAllText(Path.Combine(repo, "loose.txt"), "not added\n");

        var report = await GitChanges.ReadAsync(repo, new ProcessGitRunner());

        Assert.Equal(GitChangesKind.Changes, report.Kind);
        Assert.False(string.IsNullOrEmpty(report.Diff));
        var staged = Assert.Single(report.Files, f => f.Path == "staged.txt");
        Assert.Equal("added", staged.Label);
        Assert.Equal((2, 0), (staged.Added, staged.Deleted));
        Assert.True(Assert.Single(report.Files, f => f.Path == "img.bin").Binary);
        var loose = Assert.Single(report.Files, f => f.Path == "loose.txt");
        Assert.Equal("new, not in the diff", loose.Label);
        Assert.Null(loose.Added);
        Assert.Contains("+second", report.Diff);
    }

    [Fact]
    public async Task ADeletionStagedInTheIndexIsFlaggedByTheDeletionCheck()
    {
        if (!GitInstalled()) return;
        var repo = NewRepository("deletions");
        File.WriteAllText(Path.Combine(repo, "big.txt"), string.Concat(Enumerable.Range(1, 60).Select(i => "line " + i + "\n")));
        File.WriteAllText(Path.Combine(repo, "gone.txt"), "bye\n");
        Git(repo, "add", ".");
        Git(repo, "commit", "-q", "-m", "more");
        File.WriteAllText(Path.Combine(repo, "big.txt"), "kept\n");
        Git(repo, "add", "big.txt");
        Git(repo, "rm", "-q", "gone.txt");

        var report = await GitChanges.ReadAsync(repo, new ProcessGitRunner());

        var big = Assert.Single(report.Files, f => f.Path == "big.txt");
        // Sixty lines went and one came: the check reads the whole change against HEAD, staged or not.
        Assert.Equal((1, 60), (big.Added, big.Deleted));
        Assert.Equal("mostly deleted (-60 +1)", DeletionCheck.Flag(big));
        Assert.Equal("deleted", DeletionCheck.Flag(Assert.Single(report.Files, f => f.Path == "gone.txt")));
    }

    /// <summary>A script git would run as a hook or a driver: it writes the marker file when it runs. Returns its path with forward slashes.</summary>
    private string WriteMarkerScript(string marker)
    {
        var script = Path.Combine(_root, "marker-" + Guid.NewGuid().ToString("N")[..8] + ".sh");
        File.WriteAllText(script, "#!/bin/sh\nprintf ran > '" + marker + "'\ncat \"$1\" 2>/dev/null\nexit 0\n");
        return script.Replace('\\', '/');
    }

    [Fact]
    public async Task ARepositoryFsmonitorProgramIsNotRunForStatusDiffOrMergeTree()
    {
        if (!GitInstalled()) return;
        var repo = NewRepository("fsmonitor");
        var marker = Path.Combine(_root, "fsmonitor-ran.txt").Replace('\\', '/');
        Git(repo, "config", "core.fsmonitor", WriteMarkerScript(marker));
        File.WriteAllText(Path.Combine(repo, "a b.txt"), "changed\n");
        var git = new ProcessGitRunner();

        await GitChanges.ReadAsync(repo, git);
        await git.RunAsync(["merge-tree", "--write-tree", "--name-only", "-z", "HEAD", "HEAD"], repo, TimeSpan.FromSeconds(10), GitChanges.MaxOutputBytes);

        Assert.False(File.Exists(marker), "git ran core.fsmonitor from the repository's own config");
    }

    [Fact]
    public async Task ARepositoryTextconvDriverIsNotRunForTheChangesTab()
    {
        if (!GitInstalled()) return;
        var repo = NewRepository("textconv");
        var marker = Path.Combine(_root, "textconv-ran.txt").Replace('\\', '/');
        File.WriteAllText(Path.Combine(repo, ".gitattributes"), "*.dat diff=marker\n");
        File.WriteAllText(Path.Combine(repo, "d.dat"), "x\n");
        Git(repo, "add", ".");
        Git(repo, "commit", "-q", "-m", "driver");
        Git(repo, "config", "diff.marker.textconv", WriteMarkerScript(marker));
        File.WriteAllText(Path.Combine(repo, "d.dat"), "y\n");

        await GitChanges.ReadAsync(repo, new ProcessGitRunner());

        Assert.False(File.Exists(marker), "git ran the textconv driver from the repository's own config");
    }
}
