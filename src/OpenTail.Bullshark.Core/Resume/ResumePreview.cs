using System.Globalization;

namespace OpenTail.Bullshark.Core.Resume;

/// <summary>
/// A summary made for a resume. The resume prompt keeps it, so that the start reuses it instead of making it again: the preview and the
/// start then show and send the same summary. Level is the level it was made at. Saved is true once it was saved as a file; the save is
/// tried once, when the summary is made, and a failed save is logged and not retried.
/// </summary>
public sealed record KeptSummary(SummaryLevel Level, string Text, bool Saved)
{
    /// <summary>True only for the level the summary was made at. No level (a resume with no summary) never matches.</summary>
    public bool MatchesLevel(SummaryLevel? level) => level is { } chosen && chosen == Level;
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
