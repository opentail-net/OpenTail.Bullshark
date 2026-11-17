using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Trust;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

public sealed class PassSnapshotTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-snapshot-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _claudeJson;

    public PassSnapshotTests()
    {
        Directory.CreateDirectory(_dir);
        _claudeJson = Path.Combine(_dir, ".claude.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Repo(string name)
    {
        var root = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        return root;
    }

    private void WriteFile(string text) => File.WriteAllText(_claudeJson, text);

    [Fact]
    public void WhileTheTrustRecordCannotBeReadNothingRunsAndNoRootWaits()
    {
        var repo = Repo("repo-problem");
        var plain = Path.Combine(_dir, "plain-problem");
        Directory.CreateDirectory(plain);
        WriteFile("{\"projects\":{");

        var plan = PassSnapshot.Trust(
            [TestSpecs.Planner(repo, "t"), TestSpecs.Coder(plain)],
            ClaudeTrust.Read(_claudeJson));

        Assert.Empty(plan.Runnable);
        Assert.Empty(plan.UntrustedRoots);
        Assert.Equal(0, plan.Waiting);
        Assert.Contains("cannot be read", plan.Problem);
    }

    [Fact]
    public void AnUntrustedRepositoryWaitsAndItsSpecsAreCounted()
    {
        var trusted = Repo("trusted");
        var untrusted = Repo("untrusted");
        var plain = Path.Combine(_dir, "plain");
        Directory.CreateDirectory(plain);
        WriteFile("{\"projects\":{" +
                  $"\"{trusted.Replace('\\', '/')}\":{{\"hasTrustDialogAccepted\":true}}," +
                  $"\"{untrusted.Replace('\\', '/')}\":{{\"hasTrustDialogAccepted\":false}}" +
                  "}}");

        var plan = PassSnapshot.Trust(
            [TestSpecs.Planner(trusted, "t"), TestSpecs.Planner(untrusted, "t"), TestSpecs.Coder(untrusted), TestSpecs.Planner(plain, "t")],
            ClaudeTrust.Read(_claudeJson));

        Assert.Null(plan.Problem);
        Assert.Equal(2, plan.Runnable.Count);
        Assert.DoesNotContain(plan.Runnable, s => s.WorkingDirectory.Contains("untrusted", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(untrusted, Assert.Single(plan.UntrustedRoots));
        Assert.Equal(2, plan.Waiting);
    }

    [Fact]
    public void WhenEverythingIsTrustedNothingWaits()
    {
        var repo = Repo("all-trusted");
        WriteFile($"{{\"projects\":{{\"{repo.Replace('\\', '/')}\":{{\"hasTrustDialogAccepted\":true}}}}}}");

        var plan = PassSnapshot.Trust([TestSpecs.Planner(repo, "t"), TestSpecs.Coder(repo)], ClaudeTrust.Read(_claudeJson));

        Assert.Equal(2, plan.Runnable.Count);
        Assert.Empty(plan.UntrustedRoots);
        Assert.Equal(0, plan.Waiting);
        Assert.Null(plan.Problem);
    }

    [Fact]
    public void AMissingTrustRecordIsNotAProblemAndHoldsBackTheRepository()
    {
        var repo = Repo("no-file");

        var plan = PassSnapshot.Trust([TestSpecs.Planner(repo, "t")], ClaudeTrust.Read(_claudeJson));

        Assert.Null(plan.Problem);
        Assert.Empty(plan.Runnable);
        Assert.Equal(repo, Assert.Single(plan.UntrustedRoots));
        Assert.Equal(1, plan.Waiting);
    }

    [Fact]
    public void FailedStartsAreTheRecordsWithNoSessionId()
    {
        var at = DateTimeOffset.UnixEpoch;
        var records = new[]
        {
            new SessionRecord(SessionRole.Planner, "s1", "a", at, 0, [], null, null),
            new SessionRecord(SessionRole.Coder, string.Empty, "b", at, 0, [], null, "start failed: no"),
            new SessionRecord(SessionRole.Coder, string.Empty, "c", at, 0, [], null, "start failed: no"),
        };

        Assert.Equal(2, PassSnapshot.FailedStarts(records));
        Assert.Equal(0, PassSnapshot.FailedStarts([]));
    }

    [Fact]
    public void AQuietPassSaysOnlyTheTimeAndTheActionCountAndThatSessionsKeepRunning()
    {
        var parts = PassSnapshot.StatusParts(new DateTime(2026, 11, 17, 9, 5, 3), 0, 0, 0, null, null);

        Assert.Equal(
            ["09:05:03  0 action(s) this pass", "closing the window detaches the terminals; the sessions keep running"],
            parts);
    }

    [Fact]
    public void EveryProblemAddsItsPartBeforeTheClosingNote()
    {
        var parts = PassSnapshot.StatusParts(new DateTime(2026, 11, 17, 9, 5, 3), 2, 1, 3, "state.json is torn", "claude.json is torn");

        Assert.Equal(
            [
                "09:05:03  2 action(s) this pass",
                "1 session(s) wait for trust (see the Trust step)",
                "3 start(s) failed: see the rail, then Retry failed starts",
                "state file cannot be read, nothing is changed: state.json is torn",
                "claude.json is torn",
                "closing the window detaches the terminals; the sessions keep running",
            ],
            parts);
    }
}
