using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Ui;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Ui;

public sealed class UiSettingsFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-ui-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _path;
    private readonly ConcurrentQueue<string> _log = new();

    public UiSettingsFileTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "ui.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private void Log(string message) => _log.Enqueue(message);

    [Fact]
    public void TheFileBesideTheStateFileIsUiJson()
    {
        Assert.Equal(_path, UiSettingsFile.PathFor(Path.Combine(_dir, "state.json")));
    }

    [Fact]
    public void SettingOneKeyKeepsTheOthersAndLeavesNoTemporaryFile()
    {
        UiSettingsFile.Set(_path, "zoomPercent", JsonValue.Create(110)!, Log);
        UiSettingsFile.Set(_path, "railTheme", JsonValue.Create("Light")!, Log);

        Assert.Equal(110, (int)UiSettingsFile.Read(_path, "zoomPercent", Log)!);
        Assert.Equal("Light", (string)UiSettingsFile.Read(_path, "railTheme", Log)!);
        Assert.Equal(new[] { "ui.json" }, Directory.GetFiles(_dir).Select(f => Path.GetFileName(f)).ToArray());
        Assert.Empty(_log);
    }

    [Fact]
    public void ATruncatedFileIsMovedAsideOnceAndSavesWorkAgain()
    {
        File.WriteAllText(_path, "{\"zoomPercent\": 1");

        Assert.Null(UiSettingsFile.Read(_path, "zoomPercent", Log));

        Assert.False(File.Exists(_path));
        Assert.Equal("{\"zoomPercent\": 1", File.ReadAllText(Assert.Single(DamagedCopies())));
        Assert.Single(_log);

        UiSettingsFile.Set(_path, "zoomPercent", JsonValue.Create(125)!, Log);

        Assert.Equal(125, (int)UiSettingsFile.Read(_path, "zoomPercent", Log)!);
        Assert.Single(_log);
    }

    private string[] DamagedCopies() => Directory.GetFiles(_dir, "ui.json.bad-*");

    [Fact]
    public void ASecondDamagedFileDoesNotReplaceTheFirstCopy()
    {
        // Two damage events in the same second: the second copy gets a counter, so neither copy is overwritten (F7).
        File.WriteAllText(_path, "{\"first\": 1");
        Assert.Null(UiSettingsFile.Read(_path, "zoomPercent", Log));
        File.WriteAllText(_path, "{\"second\": 2");
        Assert.Null(UiSettingsFile.Read(_path, "zoomPercent", Log));

        var contents = DamagedCopies().OrderBy(f => f, StringComparer.Ordinal).Select(File.ReadAllText).ToList();
        Assert.Equal(2, contents.Count);
        Assert.Contains("{\"first\": 1", contents);
        Assert.Contains("{\"second\": 2", contents);
    }

    [Fact]
    public void OnlyTheNewestFiveDamagedCopiesAreKept()
    {
        for (var i = 1; i <= 7; i++)
        {
            File.WriteAllText(_path, "{\"copy\": " + i);
            Assert.Null(UiSettingsFile.Read(_path, "zoomPercent", Log));
        }

        Assert.Equal(UiSettingsFile.MaxDamagedCopies, DamagedCopies().Length);
        // The two oldest (copies 1 and 2) are the ones that went.
        var kept = DamagedCopies().Select(File.ReadAllText).ToHashSet();
        Assert.DoesNotContain("{\"copy\": 1", kept);
        Assert.DoesNotContain("{\"copy\": 2", kept);
        Assert.Contains("{\"copy\": 7", kept);
    }

    [Fact]
    public void AnEmptyFileOrOneThatIsNotAnObjectIsRecoveredToo()
    {
        foreach (var damaged in new[] { "", "   ", "[1,2]", "\"text\"" })
        {
            File.WriteAllText(_path, damaged);

            UiSettingsFile.Set(_path, "railTheme", JsonValue.Create("Dark")!, Log);

            Assert.Equal("Dark", (string)UiSettingsFile.Read(_path, "railTheme", Log)!);
        }
    }

    [Fact]
    public void AFileThatCannotBeReadIsLeftAsItIs()
    {
        File.WriteAllText(_path, "{\"zoomPercent\":110}");

        // Another process holds the file without sharing it. The file is not damaged, so it is not moved, and a save does not replace it.
        using (new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Null(UiSettingsFile.Read(_path, "zoomPercent", Log));
            UiSettingsFile.Set(_path, "railTheme", JsonValue.Create("Light")!, Log);
        }

        Assert.Empty(DamagedCopies());
        Assert.Equal("{\"zoomPercent\":110}", File.ReadAllText(_path));
        Assert.Equal(2, _log.Count);
    }

    [Fact]
    public void TwoWindowsKeepEachOthersKeys()
    {
        Parallel.Invoke(
            () => { for (var i = 0; i < 30; i++) UiSettingsFile.Set(_path, $"a{i}", JsonValue.Create(i)!, Log); },
            () => { for (var i = 0; i < 30; i++) UiSettingsFile.Set(_path, $"b{i}", JsonValue.Create(i)!, Log); });

        for (var i = 0; i < 30; i++)
        {
            Assert.Equal(i, (int)UiSettingsFile.Read(_path, $"a{i}", Log)!);
            Assert.Equal(i, (int)UiSettingsFile.Read(_path, $"b{i}", Log)!);
        }
        Assert.Empty(_log);
    }
}
