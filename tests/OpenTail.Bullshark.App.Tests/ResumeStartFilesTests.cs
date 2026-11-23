using System;
using System.IO;
using System.Threading.Tasks;
using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The two file writes the Codex and Copilot resume screens make for the window (dedup, 2026-11-23). Each test uses a state folder of its own. The checks are
/// on what is returned and written, not on the log, which other test classes can also write to.
/// </summary>
public sealed class ResumeStartFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "opentail-start-files-" + Guid.NewGuid().ToString("N"));

    public ResumeStartFilesTests()
    {
        Directory.CreateDirectory(_root);
        AppLog.FollowStateFile(Path.Combine(_root, "state.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string StatePath => Path.Combine(_root, "state.json");

    [Fact]
    public async Task ASummaryIsSavedAsAFileBesideTheStateFile()
    {
        var saved = await ResumeStartFiles.SaveSummaryAsync(StatePath, "codex", "A session", SummaryLevel.Normal, "abc123", "a short summary");

        Assert.True(saved);
        Assert.Single(Directory.GetFiles(SummaryFiles.FolderBeside(StatePath)));
    }

    [Fact]
    public async Task ASummaryThatCannotBeWrittenIsReportedAsNotSavedAndNothingIsThrown()
    {
        // A file where the Summaries folder should be: the folder cannot be made, so the save fails.
        File.WriteAllText(SummaryFiles.FolderBeside(StatePath), "not a folder");

        var saved = await ResumeStartFiles.SaveSummaryAsync(StatePath, "copilot", "A session", SummaryLevel.Normal, "abc123", "a short summary");

        Assert.False(saved);
    }

    [Fact]
    public void OldStartFilesAreLookedForInTheFolderBesideTheStateFile()
    {
        string? seen = null;

        ResumeStartFiles.RemoveOldStartsQuietly(StatePath, "codex", state => Path.GetDirectoryName(state)!, folder => { seen = folder; return 2; });

        Assert.Equal(_root, seen);
    }

    [Fact]
    public void AFailedRemovalIsLoggedAndNothingIsThrown()
    {
        var ran = false;

        ResumeStartFiles.RemoveOldStartsQuietly(StatePath, "copilot", _ => _root, _ => { ran = true; throw new IOException("the folder is busy"); });

        Assert.True(ran);
    }
}
