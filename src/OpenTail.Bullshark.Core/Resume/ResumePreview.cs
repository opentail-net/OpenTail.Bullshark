using System.Globalization;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Resume;

/// <summary>
/// A summary made for a resume. The resume prompt keeps it, so that the start reuses it instead of making it again: the preview and the
/// start then show and send the same summary. Level is the level it was made at, and Turns the tail's turns it was made for (zero when the tail is off,
/// RS-D21). Lines is the number of transcript lines the summary was made from (FIX 1 of the S6c review): a transcript that grew after the summary was
/// made gives another input, so the summary is out of date. WrittenBy is the platform whose summariser wrote it (RS-D28); null is the source's own
/// summary. Saved is true once it was saved as a file; the save is
/// tried once, when the summary is made, and a failed save is logged and not retried.
/// </summary>
public sealed record KeptSummary(SummaryLevel Level, string Text, bool Saved, int Turns = 0, int Lines = 0, ProviderId? WrittenBy = null)
{
    /// <summary>True only for the level the summary was made at. No level (a resume with no summary) never matches.</summary>
    public bool MatchesLevel(SummaryLevel? level) => level is { } chosen && chosen == Level;

    /// <summary>
    /// True only for the level, the tail and the transcript the summary was made at (RS-D21). A summary covers the messages before the tail, so another tail
    /// gives another input, and a transcript with another line count gives another input too. <paramref name="turns"/> is the tail's turns, zero when the
    /// tail is off, as <see cref="Turns"/> holds it. <paramref name="lines"/> is the transcript's line count now, as <see cref="Lines"/> holds it when made.
    /// </summary>
    public bool MatchesChoice(SummaryLevel? level, int turns, int lines) => MatchesLevel(level) && Turns == turns && Lines == lines;
}

/// <summary>
/// What the resume prompt shows for a choice's start text: the text, the characters cut from it to fit the command line, whether it fits,
/// and the summary it carries (made now or reused). Summary is null when the choice has no summary.
/// </summary>
public sealed record ResumePreview(string Text, int CutCharacters, bool Fits, KeptSummary? Summary);

/// <summary>The size line above the resume prompt's preview. Pure, so it is tested without a window.</summary>
public static class ResumePreviewText
{
    /// <summary>
    /// "Prompt the new session starts with: about N tokens (C characters)." The tokens are the characters over four, rounded. A cut is said
    /// when there was one, and a text that does not fit is said so, because Resume would not start it.
    /// </summary>
    public static string Header(int characters, int cutCharacters, bool fits)
    {
        var tokens = (int)Math.Round(characters / 4.0, MidpointRounding.AwayFromZero);
        var text = $"Prompt the new session starts with: about {Count(tokens)} tokens ({Count(characters)} characters).";
        if (cutCharacters > 0) text += $" {Count(cutCharacters)} characters were cut to fit the command line.";
        if (!fits) text += " It is too long for the command line even with its latest turn cut, so Resume would not start it.";
        return text;
    }

    private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
