using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Library;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Library;

/// <summary>
/// The "viewed" ticks of the Changes tab (features batch G1): a tick counts only while the file keeps its stamp, the cap, the age rule, the
/// drop of files that are no longer changed, and the ui.json form. Pure, except the file stamp, which is read from a scratch folder.
/// </summary>
public sealed class ViewedFilesTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private const string Session = "5e4f3a21-0000-4000-8000-00000000c0d1";
    private const string Other = "5e4f3a21-0000-4000-8000-00000000c0d2";
    private const string Stamp1 = "120:638000000000000000";
    private const string Stamp2 = "121:638000000000000001";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "bullshark-viewed-" + Guid.NewGuid().ToString("N")[..8]);

    public ViewedFilesTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static IReadOnlyDictionary<string, ViewedSession> None => new Dictionary<string, ViewedSession>(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void ATickedFileIsViewedOnlyForItsSessionAndItsPath()
    {
        var marks = ViewedFiles.Mark(None, Session, "src/a.cs", Stamp1, Now);

        Assert.True(ViewedFiles.IsViewed(marks, Session, "src/a.cs", Stamp1));
        Assert.False(ViewedFiles.IsViewed(marks, Other, "src/a.cs", Stamp1));
        Assert.False(ViewedFiles.IsViewed(marks, Session, "src/b.cs", Stamp1));
    }

    [Fact]
    public void AChangedFileIsUnviewedAgain()
    {
        // The stamp the file had when it was ticked no longer matches: the tick does not hide the new edit (contract).
        var marks = ViewedFiles.Mark(None, Session, "src/a.cs", Stamp1, Now);

        Assert.False(ViewedFiles.IsViewed(marks, Session, "src/a.cs", Stamp2));
        Assert.False(ViewedFiles.IsViewed(marks, Session, "src/a.cs", null));
    }

    [Fact]
    public void ACurrentStampOfAFileChangesWhenItsContentChanges()
    {
        var path = Path.Combine(_folder, "notes.md");
        File.WriteAllText(path, "one\n");
        var before = ViewedFiles.CurrentStamp(_folder, "notes.md");
        var marks = ViewedFiles.Mark(None, Session, "notes.md", before, Now);
        Assert.True(ViewedFiles.IsViewed(marks, Session, "notes.md", ViewedFiles.CurrentStamp(_folder, "notes.md")));

        File.AppendAllText(path, "two\n");

        var after = ViewedFiles.CurrentStamp(_folder, "notes.md");
        Assert.NotEqual(before, after);
        Assert.False(ViewedFiles.IsViewed(marks, Session, "notes.md", after));
    }

    [Fact]
    public void ADeletedFileHasTheDeletedStampUntilItIsThereAgain()
    {
        File.WriteAllText(Path.Combine(_folder, "old.md"), "x\n");
        var marks = ViewedFiles.Mark(None, Session, "gone.md", ViewedFiles.CurrentStamp(_folder, "gone.md"), Now);
        Assert.Equal(ViewedFiles.Deleted, ViewedFiles.CurrentStamp(_folder, "gone.md"));
        Assert.True(ViewedFiles.IsViewed(marks, Session, "gone.md", ViewedFiles.CurrentStamp(_folder, "gone.md")));

        File.WriteAllText(Path.Combine(_folder, "gone.md"), "back\n");

        Assert.False(ViewedFiles.IsViewed(marks, Session, "gone.md", ViewedFiles.CurrentStamp(_folder, "gone.md")));
    }

    [Fact]
    public void AFileWithNoStampIsNeverViewedAndIsNotTicked()
    {
        var marks = ViewedFiles.Mark(None, Session, "locked.cs", null, Now);

        Assert.Empty(marks);
        Assert.False(ViewedFiles.IsViewed(marks, Session, "locked.cs", null));
    }

    [Fact]
    public void MarkAllTicksEachFileWithItsOwnStampAndSkipsTheUnreadable()
    {
        var stamps = new Dictionary<string, string?> { ["a.cs"] = Stamp1, ["b.cs"] = Stamp2, ["c.cs"] = null };

        var marks = ViewedFiles.MarkAll(None, Session, stamps, Now);

        Assert.Equal(2, marks[Session].Files.Count);
        Assert.True(ViewedFiles.IsViewed(marks, Session, "a.cs", Stamp1));
        Assert.True(ViewedFiles.IsViewed(marks, Session, "b.cs", Stamp2));
        Assert.False(ViewedFiles.IsViewed(marks, Session, "c.cs", null));
    }

    [Fact]
    public void ViewedAmongListsOnlyTheFilesWhoseStampIsStillTheSame()
    {
        var marks = ViewedFiles.MarkAll(None, Session, new Dictionary<string, string?> { ["a.cs"] = Stamp1, ["b.cs"] = Stamp1 }, Now);

        var viewed = ViewedFiles.ViewedAmong(marks, Session,
            new Dictionary<string, string?> { ["a.cs"] = Stamp1, ["b.cs"] = Stamp2, ["c.cs"] = Stamp1 });

        Assert.Equal(["a.cs"], viewed.ToList());
    }

    [Fact]
    public void UnmarkRemovesOneFileAndAnEmptySessionIsDropped()
    {
        var marks = ViewedFiles.MarkAll(None, Session, new Dictionary<string, string?> { ["a.cs"] = Stamp1, ["b.cs"] = Stamp1 }, Now);

        var fewer = ViewedFiles.Unmark(marks, Session, "a.cs");
        Assert.False(ViewedFiles.IsViewed(fewer, Session, "a.cs", Stamp1));
        Assert.True(ViewedFiles.IsViewed(fewer, Session, "b.cs", Stamp1));

        var none = ViewedFiles.Unmark(fewer, Session, "b.cs");
        Assert.False(none.ContainsKey(Session));
    }

    [Fact]
    public void ClearRemovesEveryTickOfTheSessionOnly()
    {
        var marks = ViewedFiles.MarkAll(None, Session, new Dictionary<string, string?> { ["a.cs"] = Stamp1 }, Now);
        marks = ViewedFiles.MarkAll(marks, Other, new Dictionary<string, string?> { ["a.cs"] = Stamp1 }, Now);

        var cleared = ViewedFiles.Clear(marks, Session);

        Assert.False(cleared.ContainsKey(Session));
        Assert.True(ViewedFiles.IsViewed(cleared, Other, "a.cs", Stamp1));
    }

    [Fact]
    public void ASessionOlderThanNinetyDaysIsPrunedAndANewerOneIsKept()
    {
        var old = ViewedFiles.Mark(None, Session, "a.cs", Stamp1, Now.AddDays(-91));
        var recent = ViewedFiles.Mark(old, Other, "a.cs", Stamp1, Now.AddDays(-89));

        var pruned = ViewedFiles.Prune(recent, Now);

        Assert.False(pruned.ContainsKey(Session));
        Assert.True(pruned.ContainsKey(Other));
    }

    [Fact]
    public void AFileTickedAgainMovesToTheNewestPlaceAndTheCapDropsTheOldest()
    {
        var stamps = new Dictionary<string, string?>();
        for (var i = 0; i < ViewedFiles.MaxFilesPerSession + 10; i++) stamps[$"f{i}.cs"] = Stamp1;
        var marks = ViewedFiles.MarkAll(None, Session, stamps, Now);

        var files = marks[Session].Files.Select(file => file.Path).ToList();
        Assert.Equal(ViewedFiles.MaxFilesPerSession, files.Count);
        Assert.Equal("f10.cs", files[0]);
        Assert.Equal($"f{ViewedFiles.MaxFilesPerSession + 9}.cs", files[^1]);

        // Ticking the oldest file again keeps it, and drops the next oldest instead.
        var again = ViewedFiles.Mark(marks, Session, "f10.cs", Stamp1, Now);
        Assert.Equal(ViewedFiles.MaxFilesPerSession, again[Session].Files.Count);
        Assert.Equal("f10.cs", again[Session].Files[^1].Path);
        Assert.Equal("f11.cs", again[Session].Files[0].Path);
    }

    [Fact]
    public void PruneAlsoCapsASessionThatWasReadFromTheFile()
    {
        var files = new JsonObject();
        for (var i = 0; i < ViewedFiles.MaxFilesPerSession + 5; i++) files[$"f{i}.cs"] = Stamp1;
        var json = new JsonObject { [Session] = new JsonObject { ["at"] = Now.ToString("O"), ["files"] = files } };

        var marks = ViewedFiles.Prune(ViewedFiles.Parse(json), Now);

        Assert.Equal(ViewedFiles.MaxFilesPerSession, marks[Session].Files.Count);
        Assert.Equal("f5.cs", marks[Session].Files[0].Path);
    }

    [Fact]
    public void DropUnchangedKeepsOnlyTheFilesThatAreStillChanged()
    {
        var marks = ViewedFiles.MarkAll(None, Session, new Dictionary<string, string?> { ["a.cs"] = Stamp1, ["b.cs"] = Stamp1, ["c.cs"] = Stamp1 }, Now);

        var kept = ViewedFiles.DropUnchanged(marks, Session, new HashSet<string> { "a.cs" });

        Assert.Equal(["a.cs"], kept[Session].Files.Select(file => file.Path).ToList());
        var emptied = ViewedFiles.DropUnchanged(kept, Session, new HashSet<string>());
        Assert.False(emptied.ContainsKey(Session));
    }

    [Fact]
    public void TheMarksRoundTripThroughTheUiFileForm()
    {
        var marks = ViewedFiles.MarkAll(None, Session, new Dictionary<string, string?> { ["src/a b.cs"] = Stamp1, ["gone.md"] = ViewedFiles.Deleted }, Now);

        var parsed = ViewedFiles.Parse(ViewedFiles.ToJson(marks));

        Assert.Equal(Now, parsed[Session].MarkedAt);
        Assert.Equal(["src/a b.cs", "gone.md"], parsed[Session].Files.Select(file => file.Path).ToList());
        Assert.True(ViewedFiles.IsViewed(parsed, Session, "gone.md", ViewedFiles.Deleted));
    }

    [Fact]
    public void ParseReadsNothingFromAMissingOrDamagedValueAndKeepsTheRest()
    {
        var node = JsonNode.Parse("""
            {
              "nothing-here": 5,
              "bad-time": { "at": "not a date", "files": { "a.cs": "1:2" } },
              "bad-files": { "at": "2026-11-18T12:00:00Z", "files": "x" },
              "bad-stamp": { "at": "2026-11-18T12:00:00Z", "files": { "a.cs": 7, "b.cs": "3:4" } },
              "5e4f3a21-0000-4000-8000-00000000c0d1": { "at": "2026-11-18T12:00:00Z", "files": { "a.cs": "120:638000000000000000" } }
            }
            """);

        var marks = ViewedFiles.Parse(node);

        Assert.Equal(2, marks.Count);
        Assert.Equal(["a.cs"], marks[Session].Files.Select(f => f.Path).ToList());
        // A number is not a stamp: only the text one of the damaged session is kept.
        Assert.Equal(["b.cs"], marks["bad-stamp"].Files.Select(f => f.Path).ToList());
        Assert.Empty(ViewedFiles.Parse(null));
        Assert.Empty(ViewedFiles.Parse(JsonValue.Create(3)));
    }

    [Fact]
    public void CurrentStampOfAFileNeverReadsItsText()
    {
        File.WriteAllText(Path.Combine(_folder, "text.md"), "secret words\n");

        var stamp = ViewedFiles.CurrentStamp(_folder, "text.md")!;

        Assert.Matches(@"^\d+:\d+$", stamp);
        Assert.DoesNotContain("secret", stamp);
    }
}
