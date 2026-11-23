using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Ui;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Ui;

/// <summary>
/// Reliability batch R5 and R7: a change to ui.json is read, changed and written under one lock hold, so two windows keep each other's
/// marks; and a read that cannot get the lock in time says so, instead of waiting for it.
/// </summary>
public sealed class UiSettingsUpdateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-uiupd-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _path;
    private readonly ConcurrentQueue<string> _log = new();

    public UiSettingsUpdateTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "ui.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private void Log(string message) => _log.Enqueue(message);

    /// <summary>The reviewed marks as one window sees them when it changes one of them.</summary>
    private static Func<JsonNode?, JsonNode?> ReviewedAdd(string sessionId, DateTimeOffset at) => node =>
    {
        var marks = ReviewedMarks.Parse(node);
        marks[sessionId] = at;
        return ReviewedMarks.Write(marks);
    };

    [Fact]
    public void AnUpdateChangesOneKeyAndKeepsTheOthers()
    {
        UiSettingsFile.Set(_path, "zoomPercent", JsonValue.Create(110)!, Log);

        Assert.True(UiSettingsFile.Update(_path, "railTheme", node => JsonValue.Create("Light"), Log));

        Assert.Equal(110, (int)UiSettingsFile.Read(_path, "zoomPercent", Log)!);
        Assert.Equal("Light", (string)UiSettingsFile.Read(_path, "railTheme", Log)!);
    }

    [Fact]
    public void TheChangeGetsTheValueAsItIsOnDiskWhenTheKeyIsThere()
    {
        UiSettingsFile.Set(_path, "zoomPercent", JsonValue.Create(110)!, Log);
        int? seen = null;

        UiSettingsFile.Update(_path, "zoomPercent", node => { seen = (int)node!; return JsonValue.Create(125); }, Log);

        Assert.Equal(110, seen);
        Assert.Equal(125, (int)UiSettingsFile.Read(_path, "zoomPercent", Log)!);
    }

    [Fact]
    public void AWindowThatHoldsAnOldCopyDoesNotLoseAnotherWindowsMark()
    {
        // Window one read the marks when it opened: none. Window two marks a session. Window one then marks another session by an update,
        // which reads the file again under the lock: both marks stay (before R5, its whole-file write put back its old copy).
        UiSettingsFile.Update(_path, "reviewed", ReviewedAdd("session-of-two", DateTimeOffset.Parse("2026-11-19T10:00:00Z")), Log);
        UiSettingsFile.Update(_path, "reviewed", ReviewedAdd("session-of-one", DateTimeOffset.Parse("2026-11-19T11:00:00Z")), Log);

        var kept = ReviewedMarks.Parse(UiSettingsFile.Read(_path, "reviewed", Log));
        Assert.Equal(new[] { "session-of-one", "session-of-two" }, kept.Keys.Order().ToArray());
    }

    [Fact]
    public void TheViewedTicksOfTwoSessionsFromTwoWindowsAreBothKept()
    {
        var at = DateTimeOffset.Parse("2026-11-19T10:00:00Z");
        var stamp = ViewedFiles.StampOf(12, at.UtcTicks);
        // Window one ticks a file of session one; window two, which read the file before, ticks a file of session two.
        UiSettingsFile.Update(_path, ViewedFiles.Key,
            node => ViewedFiles.ToJson(ViewedFiles.Mark(ViewedFiles.Parse(node), "session-one", "a.cs", stamp, at)), Log);
        UiSettingsFile.Update(_path, ViewedFiles.Key,
            node => ViewedFiles.ToJson(ViewedFiles.Mark(ViewedFiles.Parse(node), "session-two", "b.cs", stamp, at)), Log);

        var marks = ViewedFiles.Parse(UiSettingsFile.Read(_path, ViewedFiles.Key, Log));
        Assert.True(ViewedFiles.IsViewed(marks, "session-one", "a.cs", stamp));
        Assert.True(ViewedFiles.IsViewed(marks, "session-two", "b.cs", stamp));
    }

    [Fact]
    public async Task ManyWritersAtOnceKeepEveryKey()
    {
        const int writers = 4;
        const int perWriter = 20;
        var tasks = Enumerable.Range(0, writers).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < perWriter; i++)
            {
                var key = $"w{w}-{i}";
                Assert.True(UiSettingsFile.Update(_path, key, _ => JsonValue.Create(key), Log));
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        var root = JsonNode.Parse(File.ReadAllText(_path))!.AsObject();
        Assert.Equal(writers * perWriter, root.Count);
        Assert.Empty(_log);
    }

    [Fact]
    public async Task AnUpdateThatCannotGetTheLockInTimeChangesNothing()
    {
        UiSettingsFile.Set(_path, "zoomPercent", JsonValue.Create(110)!, Log);
        using var held = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var holder = Task.Run(() =>
        {
            using (FileLock.Hold(_path))
            {
                held.Set();
                release.Wait();
            }
        });
        held.Wait();

        var written = UiSettingsFile.Update(_path, "zoomPercent", _ => JsonValue.Create(125), Log, TimeSpan.FromMilliseconds(150));

        release.Set();
        await holder;
        Assert.False(written);
        Assert.Equal(110, (int)UiSettingsFile.Read(_path, "zoomPercent", Log)!);
    }

    [Fact]
    public async Task TryReadSaysBusyWhileTheLockIsHeld()
    {
        UiSettingsFile.Set(_path, "zoomPercent", JsonValue.Create(110)!, Log);
        using var held = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var holder = Task.Run(() =>
        {
            using (FileLock.Hold(_path))
            {
                held.Set();
                release.Wait();
            }
        });
        held.Wait();

        var busy = UiSettingsFile.TryRead(_path, "zoomPercent", TimeSpan.FromMilliseconds(150), out var busyValue, Log);
        release.Set();
        await holder;
        var free =UiSettingsFile.TryRead(_path, "zoomPercent", TimeSpan.FromSeconds(2), out var freeValue, Log);

        Assert.False(busy);
        Assert.Null(busyValue);
        Assert.True(free);
        Assert.Equal(110, (int)freeValue!);
    }
}
