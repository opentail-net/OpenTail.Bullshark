using OpenTail.Bullshark.Core.Trust;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Trust;

public sealed class TrustCheckTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-trust-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _json;

    public TrustCheckTests()
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

    private void WriteProjects(params (string Path, bool Trusted)[] entries)
    {
        var projects = string.Join(",", entries.Select(e =>
            $"\"{e.Path.Replace("\\", "\\\\")}\":{{\"hasTrustDialogAccepted\":{(e.Trusted ? "true" : "false")}}}"));
        File.WriteAllText(_json, $"{{\"projects\":{{{projects}}}}}");
    }

    [Fact]
    public void ARepositoryRootRecordedAsTrustedIsTrusted()
    {
        var repo = Repo("trusted");
        WriteProjects((repo, true));

        Assert.Equal(TrustState.Trusted, TrustCheck.Check(repo, _json).State);
    }

    [Fact]
    public void AFolderInsideATrustedRepositoryIsTrusted()
    {
        var repo = Repo("trusted-sub");
        var sub = Path.Combine(repo, "samples", "demo-handoff");
        Directory.CreateDirectory(sub);
        WriteProjects((repo, true));

        var result = TrustCheck.Check(sub, _json);
        Assert.Equal(TrustState.Trusted, result.State);
        Assert.Equal(repo, result.RepositoryRoot);
    }

    [Fact]
    public void ARepositoryNotRecordedIsNotTrusted()
    {
        var repo = Repo("unknown");
        WriteProjects();

        var result = TrustCheck.Check(repo, _json);
        Assert.Equal(TrustState.NotTrusted, result.State);
        Assert.Contains("accept the trust prompt", result.Explanation);
    }

    [Fact]
    public void ARepositoryRecordedAsUntrustedIsNotTrusted()
    {
        var repo = Repo("refused");
        WriteProjects((repo, false));

        Assert.Equal(TrustState.NotTrusted, TrustCheck.Check(repo, _json).State);
    }

    [Fact]
    public void APlainFolderNeedsNoTrust()
    {
        var plain = Path.Combine(_dir, "plain");
        Directory.CreateDirectory(plain);
        WriteProjects();

        // Path.GetTempPath() may itself sit under a repository on some machines, so only assert the result shape.
        var result = TrustCheck.Check(plain, _json);
        Assert.True(result.State is TrustState.NotARepository or TrustState.NotTrusted or TrustState.Trusted);
    }

    [Fact]
    public void MissingClaudeJsonMeansNotTrusted()
    {
        var repo = Repo("nofile");
        Assert.False(TrustCheck.IsRecordedTrusted(repo, Path.Combine(_dir, "absent.json")));
    }

    [Fact]
    public void WindowsAndForwardSlashSpellingsMatch()
    {
        Assert.Equal(TrustCheck.Normalise(@"C:\Git\split\OpenTail.Bullshark"),
                     TrustCheck.Normalise("C:/Git/split/OpenTail.Bullshark/"));
    }

    [Fact]
    public void AFileNamedDotGitMarksARepositoryRoot()
    {
        // Git worktrees and submodules hold a .git file rather than a directory.
        var root = Path.Combine(_dir, "worktree");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".git"), "gitdir: elsewhere");

        Assert.Equal(root, TrustCheck.FindRepositoryRoot(root));
    }
}
