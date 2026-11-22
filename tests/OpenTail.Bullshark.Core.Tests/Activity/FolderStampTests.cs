using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// The folder stamp (R9) is made from a listing alone: the count, the newest write time and the total length of the files a folder holds. A change
/// of any of the three must show as a different stamp, and an unchanged folder must give an equal one.
/// </summary>
public sealed class FolderStampTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-stamp-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Write(string folder, string name, string text, DateTime written)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, written);
        return path;
    }

    /// <summary>The stamps of the folders of the given files, from a fresh listing of each file.</summary>
    private static Dictionary<string, FolderStamp> StampsOf(IEnumerable<string> paths) =>
        FolderStamps.Of(paths.Select(path => new FileInfo(path)));

    [Fact]
    public void ACountChangeChangesTheStamp()
    {
        var written = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        var first = Write("alpha", "one.jsonl", "a", written);
        Write("alpha", "two.jsonl", "b", written);
        var before = StampsOf(new[] { first, Path.Combine(_root, "alpha", "two.jsonl") });

        var third = Write("alpha", "three.jsonl", "c", written);
        var after = StampsOf(new[] { first, Path.Combine(_root, "alpha", "two.jsonl"), third });

        var key = FolderStamps.KeyOf(new FileInfo(first));
        Assert.Equal(2, before[key].Count);
        Assert.Equal(3, after[key].Count);
        Assert.NotEqual(before[key], after[key]);
    }

    [Fact]
    public void ANewestTimeChangeChangesTheStamp()
    {
        var written = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        var one = Write("alpha", "one.jsonl", "same", written);
        Write("alpha", "two.jsonl", "same", written);
        var key = FolderStamps.KeyOf(new FileInfo(one));
        var before = StampsOf(new[] { one, Path.Combine(_root, "alpha", "two.jsonl") })[key];

        var later = written.AddMinutes(30);
        File.SetLastWriteTimeUtc(one, later);
        var after = StampsOf(new[] { one, Path.Combine(_root, "alpha", "two.jsonl") })[key];

        Assert.Equal(before.Count, after.Count);
        Assert.Equal(before.TotalLength, after.TotalLength);
        Assert.Equal(later.Ticks, after.NewestTicks);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ALengthChangeChangesTheStampEvenWhenTheWriteTimeIsTheSame()
    {
        var written = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        var one = Write("alpha", "one.jsonl", "short", written);
        var key = FolderStampKey(one);
        var before = StampsOf(new[] { one })[key];

        File.AppendAllText(one, " and longer");
        File.SetLastWriteTimeUtc(one, written);
        var after = StampsOf(new[] { one })[key];

        Assert.Equal(before.Count, after.Count);
        Assert.Equal(before.NewestTicks, after.NewestTicks);
        Assert.Equal(new FileInfo(one).Length, after.TotalLength);
        Assert.NotEqual(before.TotalLength, after.TotalLength);
    }

    [Fact]
    public void AnUnchangedFolderGivesAnEqualStamp()
    {
        var written = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        var one = Write("alpha", "one.jsonl", "text", written);
        var two = Write("alpha", "two.jsonl", "more text", written.AddMinutes(5));

        var first = StampsOf(new[] { one, two });
        var second = StampsOf(new[] { two, one });

        Assert.Equal(first, second);
        Assert.Equal(first[FolderStamps.KeyOf(new FileInfo(one))], second[FolderStamps.KeyOf(new FileInfo(one))]);
    }

    [Fact]
    public void AFolderAndItsSubfolderAreTwoStamps()
    {
        var written = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        var main = Write("alpha", "session.jsonl", "main", written);
        var sub = Write(Path.Combine("alpha", "session", "subagents"), "agent.jsonl", "sub sub sub", written.AddMinutes(1));

        var stamps = StampsOf(new[] { main, sub });

        Assert.Equal(2, stamps.Count);
        Assert.Equal(1, stamps[FolderStamps.KeyOf(new FileInfo(main))].Count);
        Assert.Equal(1, stamps[FolderStamps.KeyOf(new FileInfo(sub))].Count);
        Assert.Equal(new FileInfo(sub).Length, stamps[FolderStamps.KeyOf(new FileInfo(sub))].TotalLength);
    }

    private static string FolderStampKey(string path) => FolderStamps.KeyOf(new FileInfo(path));
}
