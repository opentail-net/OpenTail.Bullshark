using System.Globalization;
using System.Text;
using OpenTail.Bullshark.Core.Ui;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Ui;

/// <summary>
/// Quick capture (batch B3): the header of a new file, one line per idea, flattening, the cap, empty text, a missing folder, a file that lacks a
/// final line break, a held file, and what ReadOpen counts. Every file is in a temporary folder that the test removes.
/// </summary>
public sealed class IdeaNotesTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 9, 30, 0, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "bullshark-ideas-" + Guid.NewGuid().ToString("N")[..8]);

    private string Path_ => Path.Combine(_folder, IdeaNotes.FileName);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string Stamp(DateTimeOffset at) => at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    [Fact]
    public void ANewFileGetsTheHeaderAndOneIdeaLine()
    {
        var saved = IdeaNotes.Append(Path_, "Add a dark theme", Now);

        Assert.True(saved);
        Assert.Equal($"# Ideas\n\n- [ ] {Stamp(Now)} Add a dark theme\n", File.ReadAllText(Path_));
    }

    [Fact]
    public void ASecondIdeaIsAddedBelowTheFirstWithoutAnotherHeader()
    {
        IdeaNotes.Append(Path_, "First", Now);
        IdeaNotes.Append(Path_, "Second", Now.AddMinutes(1));

        Assert.Equal($"# Ideas\n\n- [ ] {Stamp(Now)} First\n- [ ] {Stamp(Now.AddMinutes(1))} Second\n", File.ReadAllText(Path_));
    }

    [Fact]
    public void LineBreaksAndRunsOfSpacesBecomeSingleSpacesAndTheTextIsTrimmed()
    {
        IdeaNotes.Append(Path_, "  line one\r\nline two\n\tthree   four  ", Now);

        var lines = File.ReadAllLines(Path_);
        Assert.Equal(3, lines.Length);
        Assert.Equal($"- [ ] {Stamp(Now)} line one line two three four", lines[2]);
    }

    [Fact]
    public void AnIdeaLongerThanTheLimitIsCut()
    {
        IdeaNotes.Append(Path_, new string('x', IdeaNotes.MaxLength + 100), Now);

        var line = File.ReadAllLines(Path_)[2];
        var text = line[(line.IndexOf("] ", StringComparison.Ordinal) + 2 + Stamp(Now).Length + 1)..];
        Assert.Equal(IdeaNotes.MaxLength, text.Length);
    }

    [Fact]
    public void AnEmptyIdeaIsIgnoredAndWritesNothing()
    {
        var saved = IdeaNotes.Append(Path_, " \r\n\t ", Now);

        Assert.False(saved);
        Assert.False(File.Exists(Path_));
    }

    [Fact]
    public void AMissingFolderIsCreated()
    {
        var nested = Path.Combine(_folder, "deeper", "still", IdeaNotes.FileName);

        IdeaNotes.Append(nested, "Deep idea", Now);

        Assert.Contains("Deep idea", File.ReadAllText(nested));
    }

    [Fact]
    public void AnIdeaIsAddedOnItsOwnLineWhenTheFileDoesNotEndWithOne()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path_, "# Ideas\n\n- [ ] old idea");

        IdeaNotes.Append(Path_, "new idea", Now);

        Assert.Equal($"# Ideas\n\n- [ ] old idea\n- [ ] {Stamp(Now)} new idea\n", File.ReadAllText(Path_));
    }

    [Fact]
    public void TextIsKeptAsTypedApartFromTheLineBreaks()
    {
        IdeaNotes.Append(Path_, "use `code` | pipes & *stars*", Now);

        Assert.EndsWith($"- [ ] {Stamp(Now)} use `code` | pipes & *stars*\n", File.ReadAllText(Path_));
    }

    [Fact]
    public void EnsureWritesTheHeaderOnlyWhenTheFileIsMissingOrEmpty()
    {
        IdeaNotes.Ensure(Path_);
        Assert.Equal(IdeaNotes.Header, File.ReadAllText(Path_));

        IdeaNotes.Append(Path_, "kept", Now);
        IdeaNotes.Ensure(Path_);
        Assert.Contains("kept", File.ReadAllText(Path_));
    }

    [Fact]
    public void ReadOpenCountsOnlyTheUncheckedLines()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path_, "# Ideas\n\n- [ ] a\n- [x] done\n  - [ ] nested\n- [ ] c\nplain text\n", new UTF8Encoding(false));

        var open = IdeaNotes.ReadOpen(Path_);

        Assert.Equal(["- [ ] a", "- [ ] c"], open);
    }

    [Fact]
    public void ReadOpenOfAMissingFileIsEmpty()
    {
        Assert.Empty(IdeaNotes.ReadOpen(Path_));
    }

    [Fact]
    public void PathForIsIdeasMdBesideTheStateFile()
    {
        var state = Path.Combine(_folder, "state.json");

        Assert.Equal(Path.Combine(_folder, "ideas.md"), IdeaNotes.PathFor(state));
    }

    [Fact]
    public void AFileHeldByAnotherProgramThrowsIOExceptionAfterOneRetry()
    {
        Directory.CreateDirectory(_folder);
        using var held = new FileStream(Path_, FileMode.Create, FileAccess.Write, FileShare.None);

        Assert.Throws<IOException>(() => IdeaNotes.Append(Path_, "blocked", Now));
    }
}
