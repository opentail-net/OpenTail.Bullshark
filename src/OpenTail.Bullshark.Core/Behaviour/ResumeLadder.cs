namespace OpenTail.Bullshark.Core.Behaviour;

/// <summary>
/// The steps a Codex resume can take when the owner's saved session id is looked for (R7, concept note C). <see cref="Saved"/> is the saved id, and it is
/// used whenever its rollout is found. Otherwise the step is the picker (the default fallback, shown to the owner), or the newest session in the same
/// folder when <c>resume.allowNewestInFolder</c> is on and a candidate resolves.
/// </summary>
public enum ResumeStep
{
    /// <summary>The saved id's rollout is found: it is resumed, as before.</summary>
    Saved,

    /// <summary>The Codex picker (<c>codex resume</c> with no id) in a terminal tab, so the owner chooses. Never chosen by the app.</summary>
    Picker,

    /// <summary>The newest session in the same folder, labelled as not the saved one. Only when <c>resume.allowNewestInFolder</c> is on.</summary>
    NewestInFolder,

}

/// <summary>
/// The step the ladder chose. <see cref="CandidateId"/> is the session to resume for <see cref="ResumeStep.NewestInFolder"/>, and null otherwise.
/// <see cref="Label"/> is the text for the tab title and the status line.
/// </summary>
public sealed record ResumeDecision(ResumeStep Step, string? CandidateId, string Label);

/// <summary>
/// The Codex resume ladder (R7). Pure: the caller reads the rollout and the history and passes the answers in; nothing here reads a clock or the disk.
/// The ladder is: the saved id when its file is found (<see cref="ResumeStep.Saved"/>), else the picker (<see cref="ResumeStep.Picker"/>), except that
/// when <c>resume.allowNewestInFolder</c> is on and a newest-in-folder candidate resolves, that candidate takes the picker's place
/// (<see cref="ResumeStep.NewestInFolder"/>). With more than one candidate at the top rank, <c>resume.ambiguous</c> decides: "refuse" gives
/// the picker (<see cref="ResumeStep.Picker"/>), with its label saying so; "earliest" takes the earliest of the tied candidates.
/// </summary>
public static class ResumeLadder
{
    /// <summary>The label of the saved-id step.</summary>
    public const string SavedLabel = "Resumed from the saved id.";

    /// <summary>The label of the picker step.</summary>
    public const string PickerLabel = "Opened the Codex picker: the saved session was not found.";

    /// <summary>The label of the newest-in-folder step, for a single candidate.</summary>
    public const string NewestLabel = "Resumed the newest session in this folder (not the saved one).";

    /// <summary>The label of the newest-in-folder step when the earliest of several tied candidates is taken (<c>resume.ambiguous</c> "earliest").</summary>
    public const string NewestTiedLabel = "Resumed the earliest of several sessions tied as newest in this folder (not the saved one).";

    /// <summary>The label of the picker step when more than one candidate ties at the top rank and <c>resume.ambiguous</c> is "refuse".</summary>
    public const string TiedPickerLabel = "More than one session in this folder matches; the Codex picker is open.";

    /// <summary>
    /// The step for one resume. <paramref name="savedFound"/> is whether the saved id's rollout was found. <paramref name="topRankCount"/> is how many
    /// newest-in-folder candidates share the newest activity time (zero when there is none), <paramref name="newestId"/> the newest candidate's id,
    /// and <paramref name="earliestTopRankId"/> the earliest of the tied candidates (the one "earliest" takes). A saved id that is found is always
    /// used: no other step replaces it.
    /// </summary>
    public static ResumeDecision Decide(
        bool savedFound, int topRankCount, string? newestId, string? earliestTopRankId, ResumeSettings settings)
    {
        if (savedFound) return new ResumeDecision(ResumeStep.Saved, null, SavedLabel);
        if (!settings.AllowNewestInFolder || topRankCount <= 0 || newestId is null) return new ResumeDecision(ResumeStep.Picker, null, PickerLabel);
        if (topRankCount == 1) return new ResumeDecision(ResumeStep.NewestInFolder, newestId, NewestLabel);
        if (settings.Ambiguous == AmbiguousResume.Earliest && earliestTopRankId is not null)
            return new ResumeDecision(ResumeStep.NewestInFolder, earliestTopRankId, NewestTiedLabel);
        return new ResumeDecision(ResumeStep.Picker, null, TiedPickerLabel);
    }
}
