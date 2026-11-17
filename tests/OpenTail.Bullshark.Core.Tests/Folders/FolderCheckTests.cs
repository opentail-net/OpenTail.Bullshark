using OpenTail.Bullshark.Core.Folders;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Trust;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Folders;

public sealed class FolderCheckTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-folders-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _claudeJson;

    public FolderCheckTests()
    {
        Directory.CreateDirectory(_dir);
        _claudeJson = Path.Combine(_dir, ".claude.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>A folder that looks like a git repository: a .git entry, and a trust record in the claude json.</summary>
    private string Repository(string name, bool trusted)
    {
        var root = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        var spelled = root.Replace('\\', '/');
        var flag = trusted ? "true" : "false";
        File.WriteAllText(_claudeJson, "{\"projects\":{\"" + spelled + "\":{\"hasTrustDialogAccepted\":" + flag + "}}}");
        return root;
    }

    private static RunRecord Run(string folder, DateTimeOffset createdAt) =>
        new($"run-{createdAt:HHmmss}", "plan-code", folder, "auto", createdAt, []);

    [Fact]
    public void FolderKeyMatchesTheNamesClaudeWroteForRealFolders()
    {
        // Each of these was seen as a folder name under ~/.claude/projects (F44).
        Assert.Equal("C--", FolderKey.From(@"C:\"));
        Assert.Equal("C--Git-split-OpenTail-Bullshark", FolderKey.From(@"C:\Git\split\OpenTail.Bullshark"));
        Assert.Equal("C--Git-split-OpenTail-Bullshark-samples-demo-handoff",
            FolderKey.From(@"C:\Git\split\OpenTail.Bullshark\samples\demo-handoff"));
        Assert.Equal("C--Git-split-OpenTail-Bullshark--claude-worktrees-coder-hello-batch1",
            FolderKey.From(@"C:\Git\split\OpenTail.Bullshark\.claude\worktrees\coder-hello-batch1"));
    }

    [Fact]
    public void ATrailingSeparatorDoesNotChangeTheKeyButARootKeepsItsSeparator()
    {
        Assert.Equal(FolderKey.From(@"C:\Git\split\OpenTail.Bullshark"), FolderKey.From(@"C:\Git\split\OpenTail.Bullshark\"));
        Assert.Equal("C--", FolderKey.From(@"C:\"));
    }

    [Fact]
    public void KeysCompareWithoutCase()
    {
        Assert.True(FolderKey.Same(@"c:\git\split", @"C:\Git\split"));
        Assert.False(FolderKey.Same(@"C:\Git\split", @"C:\Git\other"));
    }

    [Fact]
    public void AMissingFolderIsReportedAndNothingIsWritten()
    {
        var missing = Path.Combine(_dir, "does-not-exist");

        var result = FolderCheck.Check(missing, _claudeJson);

        Assert.Equal(FolderStatus.Missing, result.Status);
        Assert.False(Directory.Exists(missing));
        Assert.False(File.Exists(_claudeJson));
    }

    [Fact]
    public void AnEmptyPathIsNotAFolder()
    {
        Assert.Equal(FolderStatus.Missing, FolderCheck.Check("", _claudeJson).Status);
    }

    [Fact]
    public void ARepositoryRecordedAsTrustedIsTrusted()
    {
        var root = Repository("trusted-repo", trusted: true);

        var result = FolderCheck.Check(root, _claudeJson);

        Assert.Equal(FolderStatus.Trusted, result.Status);
        Assert.True(FolderKey.Same(root, result.RepositoryRoot!));
    }

    [Fact]
    public void ARepositoryNotRecordedAsTrustedIsNotTrusted()
    {
        var root = Repository("untrusted-repo", trusted: false);

        var result = FolderCheck.Check(root, _claudeJson);

        Assert.Equal(FolderStatus.NotTrusted, result.Status);
        Assert.Contains("not been told to trust", result.Message);
    }

    [Fact]
    public void AFolderOutsideAnyRepositoryNeedsNoTrust()
    {
        var plain = Path.Combine(_dir, "plain");
        Directory.CreateDirectory(plain);

        Assert.Equal(FolderStatus.NotARepository, FolderCheck.Check(plain, _claudeJson).Status);
    }

    [Fact]
    public void RecentFoldersReadTheRunFileWithoutWritingIt()
    {
        var path = Path.Combine(_dir, "runs.json");

        var runs = new RunStore(path).Load();

        Assert.Empty(runs);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void SuggestionsAreTrustedRepositoriesMostRecentlyUsedFirst()
    {
        var older = Repository("older", trusted: true);
        var newer = Repository("newer", trusted: true);
        var unused = Repository("unused", trusted: true);
        var untrusted = Repository("untrusted", trusted: false);

        // Rewrite the trust record with all three trusted entries, plus the untrusted one.
        var entries = new[] { older, newer, unused }
            .Select(p => $"\"{p.Replace('\\', '/')}\":{{\"hasTrustDialogAccepted\":true}}")
            .Append($"\"{untrusted.Replace('\\', '/')}\":{{\"hasTrustDialogAccepted\":false}}");
        File.WriteAllText(_claudeJson, "{\"projects\":{" + string.Join(",", entries) + "}}");

        var runs = new[]
        {
            Run(older, new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero)),
            Run(newer, new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero)),
            Run(untrusted, new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero)),
        };

        var suggestions = FolderSuggestions.Build(runs, _claudeJson);

        Assert.Equal(3, suggestions.Count);
        Assert.Equal(FolderKey.Normalise(newer), suggestions[0]);
        Assert.Equal(FolderKey.Normalise(older), suggestions[1]);
        Assert.Equal(FolderKey.Normalise(unused), suggestions[2]);
        Assert.DoesNotContain(suggestions, s => FolderKey.Same(s, untrusted));
    }

    [Fact]
    public void WithNoTrustRecordThereAreNoSuggestions()
    {
        Assert.Empty(FolderSuggestions.Build([], _claudeJson));
    }

    [Fact]
    public void ARunWithAFolderThatIsNotAPathIsSkippedNotFatal()
    {
        // A hand-edited runs.json can hold a folder that is not a path. The suggestions are still built from the other runs.
        var good = Repository("good", trusted: true);
        var runs = new[]
        {
            Run("bad\0path", new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero)),
            Run(good, new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero)),
        };

        Assert.Equal([FolderKey.Normalise(good)], FolderSuggestions.Build(runs, _claudeJson));
    }

    [Fact]
    public void ACheckAgainstARecordReadEarlierAnswersFromThatRead()
    {
        // The setup page reads the record once and checks each key against it. A later change to the file is seen by a new read only.
        var root = Repository("repo", trusted: true);
        var earlier = ClaudeTrust.Read(_claudeJson);

        Repository("repo", trusted: false);

        Assert.Equal(FolderStatus.Trusted, FolderCheck.Check(root, earlier).Status);
        Assert.Equal(FolderStatus.NotTrusted, FolderCheck.Check(root, ClaudeTrust.Read(_claudeJson)).Status);
    }
}
