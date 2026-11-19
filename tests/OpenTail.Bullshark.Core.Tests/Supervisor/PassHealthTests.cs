using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>Reliability batch R9: a failed pass shows once, and clears when it works again; its reason names no folder.</summary>
public sealed class PassHealthTests
{
    [Fact]
    public void AWorkingPassFromTheStartChangesNothing()
    {
        var health = new PassHealth();

        Assert.Equal(PassHealthChange.None, health.Observe(null));
        Assert.Equal(PassHealthChange.None, health.Observe(null));
        Assert.Null(health.Problem);
    }

    [Fact]
    public void AFailureStartsOnceAndDoesNotRepeat()
    {
        var health = new PassHealth();

        Assert.Equal(PassHealthChange.Failed, health.Observe("runs.json cannot be read"));
        Assert.Equal(PassHealthChange.StillFailed, health.Observe("runs.json cannot be read"));
        Assert.Equal(PassHealthChange.StillFailed, health.Observe("runs.json cannot be read"));
        Assert.Equal("runs.json cannot be read", health.Problem);
    }

    [Fact]
    public void AFailureThatChangesItsReasonIsUpdatedWithoutANewStart()
    {
        var health = new PassHealth();
        health.Observe("claude could not start");

        Assert.Equal(PassHealthChange.StillFailed, health.Observe("runs.json cannot be read"));
        Assert.Equal("runs.json cannot be read", health.Problem);
    }

    [Fact]
    public void ARecoveredPassClearsTheProblemOnce()
    {
        var health = new PassHealth();
        health.Observe("state.json cannot be read");

        Assert.Equal(PassHealthChange.Recovered, health.Observe(null));
        Assert.Equal(PassHealthChange.None, health.Observe(null));
        Assert.Null(health.Problem);
    }

    [Fact]
    public void AFailureAfterRecoveryStartsAgain()
    {
        var health = new PassHealth();
        health.Observe("state.json cannot be read");
        health.Observe(null);

        Assert.Equal(PassHealthChange.Failed, health.Observe("state.json cannot be read"));
    }

    [Fact]
    public void AFullPathIsCutToItsFileName()
    {
        var reason = PassHealth.Short(@"C:\Users\Dmitri\AppData\Local\OpenTail.Bullshark\runs.json cannot be read (The process cannot access the file).");

        Assert.Equal("runs.json cannot be read (The process cannot access the file).", reason);
        Assert.DoesNotContain("Users", reason);
        Assert.DoesNotContain("Dmitri", reason);
    }

    [Fact]
    public void APathInBracketsOrQuotesIsCutToo()
    {
        var reason = PassHealth.Short(@"Could not start 'claude' in ""D:\Git\repo"" (see (C:\Git\x\.claude.json)).");

        Assert.DoesNotContain("Git", reason);
        Assert.Contains(".claude.json", reason);
        Assert.Contains("repo", reason.Split(' ', '"').Select(w => w.TrimEnd('.')));
    }

    [Fact]
    public void AUncPathKeepsOnlyItsLastPart()
    {
        var reason = PassHealth.Short(@"share \\server\folder\state.json is gone");

        Assert.Equal(@"share state.json is gone", reason);
    }

    [Fact]
    public void ALongReasonIsShortened()
    {
        var reason = PassHealth.Short(new string('x', 400));

        Assert.True(reason.Length <= PassHealth.MaxReason + 1);
        Assert.EndsWith("…", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SpacesAreCollapsed()
    {
        Assert.Equal("a b c", PassHealth.Short("a   b\r\n c"));
    }

    [Theory]
    [InlineData(@"could not read D:\Client Work\Acme\x.cs today", "could not read x.cs today")]
    [InlineData(@"D:\Client Work\Acme Ltd\state.json cannot be read", "state.json cannot be read")]
    [InlineData(@"gone: C:\Users\Dmitri\My Documents\runs.json:12", "gone: runs.json:12")]
    [InlineData(@"share \\server\Client Share\state.json is gone", "share state.json is gone")]
    public void APathWithSpacesInItsFoldersIsCutToItsFileName(string text, string expected)
    {
        var reason = PassHealth.Short(text);

        Assert.Equal(expected, reason);
        Assert.DoesNotContain("Client", reason);
        Assert.DoesNotContain("Dmitri", reason);
        Assert.DoesNotContain("Acme", reason);
    }

    [Theory]
    [InlineData("failed at /c/Users/Dmitri/Client Work/x.cs now", "failed at x.cs now")]
    [InlineData("failed at /mnt/c/Users/Dmitri/notes.md now", "failed at notes.md now")]
    [InlineData("failed at /home/dmitri/Client Work/a.txt", "failed at a.txt")]
    [InlineData("failed at /Users/dmitri/secret folder/a.txt", "failed at a.txt")]
    public void AGitBashOrUnixPathIsCutToItsFileName(string text, string expected)
    {
        var reason = PassHealth.Short(text);

        Assert.Equal(expected, reason);
        Assert.DoesNotContain("Dmitri", reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Client", reason);
        Assert.DoesNotContain("secret", reason);
    }

    [Fact]
    public void ATextWithoutAPathIsUnchanged()
    {
        // A URL is not a path (its scheme's last letter is not a drive), and a word with a slash between words is not one either.
        Assert.Equal("see https://mcp.example.invalid/x and/or a/b", PassHealth.Short("see https://mcp.example.invalid/x and/or a/b"));
    }
}
