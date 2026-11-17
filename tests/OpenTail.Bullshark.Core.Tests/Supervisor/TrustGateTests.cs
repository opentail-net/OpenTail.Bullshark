using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Trust;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

public sealed class TrustGateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-gate-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _claudeJson;

    public TrustGateTests()
    {
        Directory.CreateDirectory(_dir);
        _claudeJson = Path.Combine(_dir, ".claude.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void AnUntrustedRunWaitsAndTheOtherRunsGoOn()
    {
        var trusted = Path.Combine(_dir, "trusted");
        var untrusted = Path.Combine(_dir, "untrusted");
        var plain = Path.Combine(_dir, "plain");
        foreach (var repo in new[] { trusted, untrusted }) Directory.CreateDirectory(Path.Combine(repo, ".git"));
        Directory.CreateDirectory(plain);
        File.WriteAllText(_claudeJson,
            "{\"projects\":{" +
            $"\"{trusted.Replace('\\', '/')}\":{{\"hasTrustDialogAccepted\":true}}," +
            $"\"{untrusted.Replace('\\', '/')}\":{{\"hasTrustDialogAccepted\":false}}" +
            "}}");

        var specs = new[]
        {
            TestSpecs.Planner(trusted, "t"), TestSpecs.Coder(trusted),
            TestSpecs.Planner(untrusted, "t"), TestSpecs.Coder(untrusted),
            TestSpecs.Planner(plain, "t"),
        };

        var (runnable, roots) = TrustGate.Filter(specs, _claudeJson);

        Assert.Equal(3, runnable.Count);
        Assert.DoesNotContain(runnable, s => s.WorkingDirectory.Contains("untrusted", StringComparison.OrdinalIgnoreCase));
        var root = Assert.Single(roots);
        Assert.Contains("untrusted", root, StringComparison.OrdinalIgnoreCase);
    }

    private void WriteFile(string text) => File.WriteAllText(_claudeJson, text);

    [Fact]
    public void TheReadOnceOverloadSplitsTheSameWayAsThePathOverload()
    {
        var trusted = Path.Combine(_dir, "trusted");
        var untrusted = Path.Combine(_dir, "untrusted");
        var plain = Path.Combine(_dir, "plain");
        foreach (var repo in new[] { trusted, untrusted }) Directory.CreateDirectory(Path.Combine(repo, ".git"));
        Directory.CreateDirectory(plain);
        WriteFile("{\"projects\":{" +
                  $"\"{trusted.Replace('\\', '/')}\":{{\"hasTrustDialogAccepted\":true}}," +
                  $"\"{untrusted.Replace('\\', '/')}\":{{\"hasTrustDialogAccepted\":false}}" +
                  "}}");
        var specs = new[] { TestSpecs.Planner(trusted, "t"), TestSpecs.Planner(untrusted, "t"), TestSpecs.Planner(plain, "t") };

        var byPath = TrustGate.Filter(specs, _claudeJson);
        var byRead = TrustGate.Filter(specs, ClaudeTrust.Read(_claudeJson));

        Assert.Equal(byPath.Runnable.Select(s => s.WorkingDirectory), byRead.Runnable.Select(s => s.WorkingDirectory));
        Assert.Equal(byPath.UntrustedRoots, byRead.UntrustedRoots);
        Assert.Equal(2, byRead.Runnable.Count);
    }

    [Fact]
    public void SeveralFoldersInOneUntrustedRepositoryGiveOneRoot()
    {
        var repo = Path.Combine(_dir, "untrusted-repo");
        var first = Path.Combine(repo, "a");
        var second = Path.Combine(repo, "b");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        WriteFile("{\"projects\":{}}");

        var (runnable, roots) = TrustGate.Filter(
            [TestSpecs.Planner(first, "t"), TestSpecs.Coder(second)],
            ClaudeTrust.Read(_claudeJson));

        Assert.Empty(runnable);
        Assert.Equal(repo, Assert.Single(roots));
    }

    [Fact]
    public void AnUnreadableFileHoldsBackARepositoryButNotAPlainFolder()
    {
        // With the file unreadable, the rule of the window (no start while there is a problem) is enforced by PassSnapshot.
        // TrustGate itself still lets a folder outside any repository run, because it needs no trust.
        var repo = Path.Combine(_dir, "repo-unreadable");
        var plain = Path.Combine(_dir, "plain-unreadable");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        Directory.CreateDirectory(plain);
        WriteFile("{\"projects\":{\"x\":{\"hasTrustDialogAc");

        var trust = ClaudeTrust.Read(_claudeJson);
        var (runnable, roots) = TrustGate.Filter([TestSpecs.Planner(repo, "t"), TestSpecs.Planner(plain, "t")], trust);

        Assert.NotNull(trust.Problem);
        Assert.Equal(plain, Assert.Single(runnable).WorkingDirectory);
        Assert.Equal(repo, Assert.Single(roots));
    }

    [Fact]
    public void EveryFolderOfOnePassIsJudgedOnTheSameRead()
    {
        var first = Path.Combine(_dir, "first");
        var second = Path.Combine(_dir, "second");
        Directory.CreateDirectory(Path.Combine(first, ".git"));
        Directory.CreateDirectory(Path.Combine(second, ".git"));
        WriteFile($"{{\"projects\":{{\"{first.Replace('\\', '/')}\":{{\"hasTrustDialogAccepted\":false}}}}}}");
        var trust = ClaudeTrust.Read(_claudeJson);

        // Claude records trust for both repositories while the pass is judging them.
        WriteFile("{\"projects\":{" +
                  $"\"{first.Replace('\\', '/')}\":{{\"hasTrustDialogAccepted\":true}}," +
                  $"\"{second.Replace('\\', '/')}\":{{\"hasTrustDialogAccepted\":true}}" +
                  "}}");
        var (runnable, roots) = TrustGate.Filter([TestSpecs.Planner(first, "t"), TestSpecs.Planner(second, "t")], trust);

        // Both answers come from the first read, so both repositories wait. A second read would have let the second one run.
        Assert.Empty(runnable);
        Assert.Equal(2, roots.Count);
    }
}
