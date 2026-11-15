namespace OpenTail.Bullshark.Core.Plan;

public sealed record PlanItem(bool Done, string Text);

public sealed record PlanBatch(int Number, string Title, IReadOnlyList<PlanItem> Items)
{
    public bool IsComplete => Items.All(i => i.Done);
}

/// <summary>
/// The shared plan in <c>docs/bullshark/PLAN.md</c>: batches as <c>## Batch N - title</c> headings, each with
/// checkbox items. The planner writes batches; the coder ticks the boxes (README, Handoff).
/// </summary>
public static class PlanFile
{
    public const string RelativePath = "docs/bullshark/PLAN.md";

    public static IReadOnlyList<PlanBatch> Parse(string text)
    {
        var batches = new List<PlanBatch>();
        int? number = null;
        string title = string.Empty;
        var items = new List<PlanItem>();

        void Flush()
        {
            if (number is int n) batches.Add(new PlanBatch(n, title, items.ToList()));
            items.Clear();
        }

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            var heading = System.Text.RegularExpressions.Regex.Match(line, @"^##\s+Batch\s+(\d+)\s*[-–:]?\s*(.*)$");
            if (heading.Success)
            {
                Flush();
                number = int.Parse(heading.Groups[1].Value);
                title = heading.Groups[2].Value.Trim();
                continue;
            }

            var box = System.Text.RegularExpressions.Regex.Match(line, @"^\s*[-*]\s+\[( |x|X)\]\s+(.*)$");
            if (box.Success && number is not null)
                items.Add(new PlanItem(Done: box.Groups[1].Value != " ", Text: box.Groups[2].Value.Trim()));
        }
        Flush();
        return batches;
    }

    /// <summary>The first batch with an unticked item, or null when the plan is finished.</summary>
    public static PlanBatch? NextOpenBatch(IReadOnlyList<PlanBatch> batches) =>
        batches.FirstOrDefault(b => !b.IsComplete);

    /// <summary>
    /// The live message the planner sends the coder with `SendMessage`. The batch travels in the message itself:
    /// no file is used to pass messages between sessions (owner decision, 2026-11-14).
    /// </summary>
    public static string HandoffMessage(PlanBatch batch)
    {
        var items = string.Join("\n", batch.Items.Select(i => $"- [{(i.Done ? "x" : " ")}] {i.Text}"));
        return $"Batch {batch.Number} - {batch.Title}\n{items}\nWhen every item is done, send the report back to the planner with SendMessage.";
    }
}
