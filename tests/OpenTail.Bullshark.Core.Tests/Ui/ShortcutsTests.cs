using OpenTail.Bullshark.Core.Ui;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Ui;

/// <summary>The shortcut table (firstrun batch B7): no chord twice in one scope, and the transcript hint is built from the table.</summary>
public sealed class ShortcutsTests
{
    [Fact]
    public void NoChordAppearsTwiceInOneScope()
    {
        Assert.Empty(Shortcuts.Duplicates());
    }

    [Fact]
    public void TheTranscriptHintIsBuiltFromTheTable()
    {
        Assert.Equal("[ ] prompts   { } failures   Alt+[ ] writes   Ctrl+[ ] compactions   Ctrl+F find", Shortcuts.TranscriptHint);
    }

    [Fact]
    public void EveryHintLabelIsOnATranscriptRow()
    {
        Assert.All(Shortcuts.All.Where(shortcut => shortcut.HintLabel is not null), shortcut => Assert.Equal(Shortcuts.Transcript, shortcut.Where));
    }

    [Fact]
    public void TheWindowChordsAreTheOnesTheSheetNames()
    {
        var chords = Shortcuts.In(Shortcuts.Window).Select(shortcut => shortcut.Chord).ToList();

        Assert.Equal(new[] { "Ctrl+Shift+P", "Ctrl+Shift+N", "Ctrl+plus", "Ctrl+minus", "Ctrl+0" }, chords);
    }

    [Fact]
    public void EveryRowHasAChordAndAMeaning()
    {
        Assert.All(Shortcuts.All, shortcut =>
        {
            Assert.False(string.IsNullOrWhiteSpace(shortcut.Chord));
            Assert.False(string.IsNullOrWhiteSpace(shortcut.Does));
            Assert.False(string.IsNullOrWhiteSpace(shortcut.Where));
        });
    }

    [Fact]
    public void TheDuplicateFinderSeesADuplicateInOneScopeOnly()
    {
        // The finder is the test's own: the table is checked as it is; this proves the finder would report one.
        var rows = new[] { new Shortcut("X", "a", Shortcuts.Window), new Shortcut("X", "b", Shortcuts.Window), new Shortcut("X", "c", Shortcuts.Find) };

        var duplicates = rows.GroupBy(shortcut => (shortcut.Where, shortcut.Chord)).Where(group => group.Count() > 1).ToList();

        Assert.Single(duplicates);
    }
}
