using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Providers;

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

    /// <summary>
    /// The messages of one session, read from its rollout (step R1b). The rollout is found by its id (<see cref="CodexTranscriptReader"/>), read
    /// line by line, and its overlong lines are left out. The id is checked first, so an id that could widen the search throws
    /// <see cref="ArgumentException"/>; a session with no rollout throws <see cref="FileNotFoundException"/>. The file is read on the calling thread:
    /// callers run it off the UI thread.
    /// </summary>
    public static List<TranscriptLine> ReadSession(ProviderPaths paths, string sessionId, CancellationToken ct)
    {
        CodexTranscriptReader.RequireSafeId(sessionId);
        var path = new CodexTranscriptReader(paths).FindRollout(sessionId)
            ?? throw new FileNotFoundException($"No rollout for session {sessionId}.", sessionId + ".jsonl");
        using var reader = RolloutReader.Open(path);
        var lines = RolloutReader.Lines(reader, ct).Where(line => !line.Overlong).Select(line => line.Text);
        return FromRollout(lines);
    }
}
