namespace OpenTail.Bullshark.Core.Activity;

/// <summary>What a jump key goes to: a typed prompt, a failed tool result, a file write, or a compaction (T5 batch).</summary>
public enum MarkKind
{
    Prompt,
    Failure,
    Write,
    Compaction,
}

/// <summary>
/// The lines of a transcript the jump keys go to, by kind, each list ascending and one-based as the view numbers lines. They are read in the same pass
/// as the turn facts (<see cref="TurnFacts.ReadWithMarks(IEnumerable{string})"/>), so a jump does not read the file again.
/// </summary>
public sealed record TranscriptMarks(IReadOnlyList<int> Prompts, IReadOnlyList<int> Failures, IReadOnlyList<int> Writes, IReadOnlyList<int> Compactions)
{
    /// <summary>No marks: the file is not read yet, or it has none of them.</summary>
    public static TranscriptMarks None { get; } = new([], [], [], []);

    /// <summary>The lines of one kind.</summary>
    public IReadOnlyList<int> Of(MarkKind kind) => kind switch
    {
        MarkKind.Prompt => Prompts,
        MarkKind.Failure => Failures,
        MarkKind.Write => Writes,
        _ => Compactions,
    };

    /// <summary>The first line of the list after <paramref name="line"/>, or null when there is none.</summary>
    public static int? Next(IReadOnlyList<int> lines, int line)
    {
        var low = 0;
        var high = lines.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (lines[middle] <= line) low = middle + 1;
            else high = middle;
        }
        return low < lines.Count ? lines[low] : null;
    }

    /// <summary>The last line of the list before <paramref name="line"/>, or null when there is none.</summary>
    public static int? Previous(IReadOnlyList<int> lines, int line)
    {
        var low = 0;
        var high = lines.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (lines[middle] < line) low = middle + 1;
            else high = middle;
        }
        return low > 0 ? lines[low - 1] : null;
    }
}
