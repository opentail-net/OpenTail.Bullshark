using OpenTail.Bullshark.Core.Trust;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Trust;

/// <summary>
/// The read-once trust record. The rows of the TrustCheck contract are checked against the new overloads, so the path-taking
/// methods (which now delegate to them) keep the same answers.
/// </summary>
public sealed class ClaudeTrustTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-claudetrust-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _json;

    public ClaudeTrustTests()
    {
        Directory.CreateDirectory(_dir);
        _json = Path.Combine(_dir, ".claude.json");
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

    /// <summary>Writes the file with the paths as JSON keys, forward slashes, the way Claude's own file reads them.</summary>
    private void WriteProjects(params (string Path, bool Trusted)[] entries)
    {
        var projects = string.Join(",", entries.Select(e =>
            $"\"{e.Path.Replace('\\', '/')}\":{{\"hasTrustDialogAccepted\":{(e.Trusted ? "true" : "false")}}}"));
        File.WriteAllText(_json, $"{{\"projects\":{{{projects}}}}}");
    }

    [Fact]
    public void ARepositoryRootRecordedAsTrustedIsTrusted()
    {
        var repo = Repo("trusted");
        WriteProjects((repo, true));

        Assert.Equal(TrustState.Trusted, TrustCheck.Check(repo, ClaudeTrust.Read(_json)).State);
    }

    [Fact]
    public void AFolderInsideATrustedRepositoryIsTrusted()
    {
        var repo = Repo("trusted-sub");
        var sub = Path.Combine(repo, "samples", "demo-handoff");
        Directory.CreateDirectory(sub);
        WriteProjects((repo, true));

        var result = TrustCheck.Check(sub, ClaudeTrust.Read(_json));
        Assert.Equal(TrustState.Trusted, result.State);
        Assert.Equal(repo, result.RepositoryRoot);
    }

    [Fact]
    public void ARepositoryNotRecordedIsNotTrustedAndSaysToAcceptTrust()
    {
        var repo = Repo("unknown");
        WriteProjects();

        var result = TrustCheck.Check(repo, ClaudeTrust.Read(_json));
        Assert.Equal(TrustState.NotTrusted, result.State);
        Assert.Contains("accept the trust prompt", result.Explanation);
    }

    [Fact]
    public void ARepositoryRecordedAsUntrustedIsNotTrusted()
    {
        var repo = Repo("refused");
        WriteProjects((repo, false));

        Assert.Equal(TrustState.NotTrusted, TrustCheck.Check(repo, ClaudeTrust.Read(_json)).State);
    }

    [Fact]
    public void APlainFolderIsNotARepository()
    {
        var plain = Path.Combine(_dir, "plain");
        Directory.CreateDirectory(plain);
        WriteProjects();

        // Path.GetTempPath() may itself sit under a repository on some machines, so only assert when no ancestor has a .git.
        var result = TrustCheck.Check(plain, ClaudeTrust.Read(_json));
        if (TrustCheck.FindRepositoryRoot(plain) is null)
            Assert.Equal(TrustState.NotARepository, result.State);
        else
            Assert.Equal(TrustCheck.FindRepositoryRoot(plain), result.RepositoryRoot);
    }

    [Fact]
    public void AMissingFileTrustsNothingAndIsNoProblem()
    {
        var repo = Repo("nofile");
        var trust = ClaudeTrust.Read(Path.Combine(_dir, "absent.json"));

        Assert.Null(trust.Problem);
        Assert.False(trust.IsTrusted(repo));
        Assert.Equal(TrustState.NotTrusted, TrustCheck.Check(repo, trust).State);
    }

    [Fact]
    public void AForwardSlashEntryMatchesABackslashRoot()
    {
        var repo = Repo("spelling");
        WriteProjects((repo, true));

        // WriteProjects writes forward slashes; the repository root comes back with backslashes on Windows.
        Assert.True(ClaudeTrust.Read(_json).IsTrusted(repo.Replace('/', '\\')));
    }

    [Fact]
    public void TheFirstEntryWinsWhenTwoSpellingsDifferOnlyInCase_UntrustedFirst()
    {
        var repo = Repo("case-a");
        File.WriteAllText(_json,
            "{\"projects\":{" +
            $"\"{repo.ToUpperInvariant().Replace('\\', '/')}\":{{\"hasTrustDialogAccepted\":false}}," +
            $"\"{repo.ToLowerInvariant().Replace('\\', '/')}\":{{\"hasTrustDialogAccepted\":true}}" +
            "}}");

        Assert.Equal(TrustState.NotTrusted, TrustCheck.Check(repo, ClaudeTrust.Read(_json)).State);
    }

    [Fact]
    public void TheFirstEntryWinsWhenTwoSpellingsDifferOnlyInCase_TrustedFirst()
    {
        var repo = Repo("case-b");
        File.WriteAllText(_json,
            "{\"projects\":{" +
            $"\"{repo.ToLowerInvariant().Replace('\\', '/')}\":{{\"hasTrustDialogAccepted\":true}}," +
            $"\"{repo.ToUpperInvariant().Replace('\\', '/')}\":{{\"hasTrustDialogAccepted\":false}}" +
            "}}");

        Assert.Equal(TrustState.Trusted, TrustCheck.Check(repo, ClaudeTrust.Read(_json)).State);
    }

    [Fact]
    public void AHalfWrittenFileGivesAProblemAndTrustsNoRoot()
    {
        var repo = Repo("half");
        File.WriteAllText(_json, $"{{\"projects\":{{\"{repo.Replace('\\', '/')}\":{{\"hasTrustDialogAccepted\":tru");

        var trust = ClaudeTrust.Read(_json);

        Assert.NotNull(trust.Problem);
        Assert.Contains("cannot be read", trust.Problem);
        Assert.Contains("no session starts or restarts", trust.Problem);
        Assert.False(trust.IsTrusted(repo));
        Assert.Equal(TrustState.NotTrusted, TrustCheck.Check(repo, trust).State);
    }

    [Fact]
    public void AFileThatIsValidJsonButNotAnObjectTrustsNothingAndIsNoProblem()
    {
        var repo = Repo("array-root");
        File.WriteAllText(_json, "[]");

        var trust = ClaudeTrust.Read(_json);

        Assert.Null(trust.Problem);
        Assert.False(trust.IsTrusted(repo));
    }

    [Fact]
    public void ANoProjectsObjectOrAProjectsArrayTrustsNothing()
    {
        var repo = Repo("no-projects");

        File.WriteAllText(_json, "{\"numStartups\":3}");
        Assert.False(ClaudeTrust.Read(_json).IsTrusted(repo));

        File.WriteAllText(_json, "{\"projects\":[]}");
        Assert.False(ClaudeTrust.Read(_json).IsTrusted(repo));
    }

    [Fact]
    public void AProjectEntryThatIsNotAnObjectIsNotTrusted()
    {
        var repo = Repo("scalar-entry");
        File.WriteAllText(_json, $"{{\"projects\":{{\"{repo.Replace('\\', '/')}\":5}}}}");

        var trust = ClaudeTrust.Read(_json);

        Assert.Null(trust.Problem);
        Assert.False(trust.IsTrusted(repo));
    }

    [Fact]
    public void AnAnswerTakenBeforeAWriteDoesNotChangeWhenTheFileChanges()
    {
        // The point of reading once: a pass keeps the answer it started with, even if Claude records trust meanwhile.
        var repo = Repo("changed-later");
        WriteProjects((repo, false));
        var trust = ClaudeTrust.Read(_json);

        WriteProjects((repo, true));

        Assert.False(trust.IsTrusted(repo));
        Assert.True(ClaudeTrust.Read(_json).IsTrusted(repo));
    }
}
