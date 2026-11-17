using OpenTail.Bullshark.Core.Tests.Support;
using OpenTail.Bullshark.Core.Trust;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Trust;

public sealed class UnreadableClaudeJsonTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-unreadable-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _json;

    public UnreadableClaudeJsonTests()
    {
        Directory.CreateDirectory(_dir);
        _json = Path.Combine(_dir, ".claude.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void AMissingSettingsFileIsNotAProblem()
    {
        // A missing file means Claude has never run here: that is "not trusted", not an unreadable file.
        Assert.Null(TrustCheck.UnreadableProblem(_json));
    }

    [Fact]
    public void AReadableSettingsFileIsNotAProblem()
    {
        File.WriteAllText(_json, "{\"projects\":{}}");

        Assert.Null(TrustCheck.UnreadableProblem(_json));
    }

    [Fact]
    public void AHalfWrittenSettingsFileIsReportedAsUnreadable()
    {
        File.WriteAllText(_json, "{\"projects\":{\"C:/Git/x\":{\"hasTrustDialogAc");

        var problem = TrustCheck.UnreadableProblem(_json);

        Assert.NotNull(problem);
        Assert.Contains("cannot be read", problem);
        Assert.Contains("no session starts or restarts", problem);
    }

    [Fact]
    public void AFileInAFolderTheUserCannotReachIsAProblemAndNotAMissingFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        File.WriteAllText(_json, "{\"projects\":{}}");

        using (Unreadable.FolderWithoutMe(_dir))
        {
            // File.Exists is false here, so the old read took the file for a missing one: nothing trusted, and no problem to report (round 5 item 17).
            Assert.NotNull(TrustCheck.UnreadableProblem(_json));
        }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("{\"projects\":{\"C:/Git/a\":true,\"C:/Git/b\":{\"hasTrustDialogAccepted\":true}}}")]
    public void TrustedRepositoriesSkipsWhatIsNotAnObject(string json)
    {
        File.WriteAllText(_json, json);

        var trusted = TrustCheck.TrustedRepositories(_json);

        Assert.True(trusted.Count == 0 || trusted.SequenceEqual(["C:/Git/b"]));
    }
}
