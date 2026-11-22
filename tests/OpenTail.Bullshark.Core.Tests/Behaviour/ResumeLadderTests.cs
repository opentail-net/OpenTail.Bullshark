using OpenTail.Bullshark.Core.Behaviour;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Behaviour;

/// <summary>
/// The Codex resume ladder (R7): which step a resume takes, and its label, from whether the saved id's file was found, the candidates at the top rank
/// and the two knobs (<c>resume.allowNewestInFolder</c>, <c>resume.ambiguous</c>). Pure: no file is read and no clock is used. The ids are synthetic.
/// </summary>
public sealed class ResumeLadderTests
{
    private const string Newest = "00000000-0000-7000-8000-00000000000a";
    private const string Earliest = "00000000-0000-7000-8000-00000000000b";

    private static ResumeSettings Knobs(bool allowNewest, AmbiguousResume ambiguous = AmbiguousResume.Refuse) =>
        new() { AllowNewestInFolder = allowNewest, Ambiguous = ambiguous };

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void ASavedIdThatIsFoundIsAlwaysResumedAndNothingReplacesIt(bool allowNewest, int topRankCount)
    {
        foreach (var ambiguous in new[] { AmbiguousResume.Refuse, AmbiguousResume.Earliest })
        {
            var choice = ResumeLadder.Decide(true, topRankCount, Newest, Earliest, Knobs(allowNewest, ambiguous));

            Assert.Equal(ResumeStep.Saved, choice.Step);
            Assert.Null(choice.CandidateId);
            Assert.Equal(ResumeLadder.SavedLabel, choice.Label);
        }
    }

    [Fact]
    public void WithTheNewestStepOffThePickerIsTheFallbackEvenWithACandidate()
    {
        // The default: resume.allowNewestInFolder is false, so the newest session is never taken; the owner sees the picker.
        var choice = ResumeLadder.Decide(false, 1, Newest, Newest, Knobs(allowNewest: false));

        Assert.Equal(ResumeStep.Picker, choice.Step);
        Assert.Null(choice.CandidateId);
        Assert.Equal(ResumeLadder.PickerLabel, choice.Label);
    }

    [Fact]
    public void WithTheNewestStepOnButNoCandidateThePickerIsTheFallback()
    {
        var choice = ResumeLadder.Decide(false, 0, null, null, Knobs(allowNewest: true));

        Assert.Equal(ResumeStep.Picker, choice.Step);
        Assert.Null(choice.CandidateId);
    }

    [Fact]
    public void WithTheNewestStepOnASingleCandidateItIsResumedAndLabelled()
    {
        var choice = ResumeLadder.Decide(false, 1, Newest, Newest, Knobs(allowNewest: true));

        Assert.Equal(ResumeStep.NewestInFolder, choice.Step);
        Assert.Equal(Newest, choice.CandidateId);
        Assert.Equal(ResumeLadder.NewestLabel, choice.Label);
    }

    [Fact]
    public void ATieAtTheTopRankOpensThePickerByDefault()
    {
        var choice = ResumeLadder.Decide(false, 2, Newest, Earliest, Knobs(allowNewest: true));

        Assert.Equal(ResumeStep.Picker, choice.Step);
        Assert.Null(choice.CandidateId);
        Assert.Equal(ResumeLadder.TiedPickerLabel, choice.Label);
    }

    [Fact]
    public void ATieAtTheTopRankTakesTheEarliestWhenTheKnobSaysSo()
    {
        var choice = ResumeLadder.Decide(false, 3, Newest, Earliest, Knobs(allowNewest: true, AmbiguousResume.Earliest));

        Assert.Equal(ResumeStep.NewestInFolder, choice.Step);
        Assert.Equal(Earliest, choice.CandidateId);
        Assert.Equal(ResumeLadder.NewestTiedLabel, choice.Label);
    }

    [Fact]
    public void ATieWithNoEarliestIdIsRefusedEvenWhenEarliestIsSet()
    {
        var choice = ResumeLadder.Decide(false, 2, Newest, null, Knobs(allowNewest: true, AmbiguousResume.Earliest));

        Assert.Equal(ResumeStep.Picker, choice.Step);
        Assert.Null(choice.CandidateId);
    }

    [Fact]
    public void AnAmbiguousTieIsNotTakenWhileTheNewestStepIsOff()
    {
        // With the newest step off, a tie never reaches the newest step, so it is the picker, not a refusal.
        var choice = ResumeLadder.Decide(false, 2, Newest, Earliest, Knobs(allowNewest: false, AmbiguousResume.Refuse));

        Assert.Equal(ResumeStep.Picker, choice.Step);
    }

    [Fact]
    public void TheDefaultSettingsGiveThePickerAndNeverTheNewestStep()
    {
        var choice = ResumeLadder.Decide(false, 1, Newest, Newest, BehaviourSettings.Defaults.Resume);

        Assert.Equal(ResumeStep.Picker, choice.Step);
    }

    [Fact]
    public void TheLabelsAreTheWordsTheOwnerSees()
    {
        Assert.Equal("Resumed from the saved id.", ResumeLadder.SavedLabel);
        Assert.Equal("Opened the Codex picker: the saved session was not found.", ResumeLadder.PickerLabel);
        Assert.Equal("Resumed the newest session in this folder (not the saved one).", ResumeLadder.NewestLabel);
        Assert.Equal("More than one session in this folder matches; the Codex picker is open.", ResumeLadder.TiedPickerLabel);
    }

    [Theory]
    [InlineData("{\"resume\":{\"allowNewestInFolder\":true,\"ambiguous\":\"earliest\"}}", ResumeStep.NewestInFolder, Earliest)]
    [InlineData("{\"resume\":{\"allowNewestInFolder\":true,\"ambiguous\":\"refuse\"}}", ResumeStep.Picker, null)]
    [InlineData("{\"resume\":{\"allowNewestInFolder\":false,\"ambiguous\":\"earliest\"}}", ResumeStep.Picker, null)]
    [InlineData("{}", ResumeStep.Picker, null)]
    public void TheKnobsInBehaviourJsonDriveTheLadder(string json, ResumeStep step, string? candidate)
    {
        // The knobs are read by the existing store, not by the ladder: this checks the two agree on the names and the defaults.
        var settings = BehaviourStore.Parse(json).Settings.Resume;

        var choice = ResumeLadder.Decide(false, 2, Newest, Earliest, settings);

        Assert.Equal(step, choice.Step);
        Assert.Equal(candidate, choice.CandidateId);
    }
}
