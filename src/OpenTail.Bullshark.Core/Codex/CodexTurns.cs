using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// The messages of a Codex rollout as transcript lines (step R1, decision R-D5). Each message of the owner or of Codex becomes one line: its
/// <c>Kind</c> is <c>you</c> or <c>codex</c>, its <c>Number</c> counts the kept messages from 1 in file order, and its <c>Time</c> is the record's
/// own timestamp (null when there is none). Messages are read by the same reader as the summary input (<see cref="CodexMessages"/>), so a summary
/// and a start text cannot disagree about what was said. Tool calls and results are not included in this version.
/// </summary>
public static class CodexTurns
{
    /// <summary>
    /// The messages of a rollout, from the texts of its lines. Pass the texts that <c>RolloutReader.Lines</c> gives: an overlong line has empty
    /// text there, so it is skipped like a bad line. A line that is not a message is skipped.
    /// </summary>
    public static List<TranscriptLine> FromRollout(IEnumerable<string> lines)
    {
        var turns = new List<TranscriptLine>();
        foreach (var line in lines)
        {
            if (CodexMessages.ReadMessage(line) is not { } message) continue;
            turns.Add(new TranscriptLine(turns.Count + 1, message.Label, message.Time, message.Text, Collapsed: false));
        }
        return turns;
    }
}
