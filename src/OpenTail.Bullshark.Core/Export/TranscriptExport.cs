using System.Text;
using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Export;

/// <summary>
/// Writes a past session as Markdown (additions item 7). It reuses <see cref="TranscriptLine"/>, the reader the transcript tab shows,
/// so the export lists the same lines the owner reads, with no second parser. Thinking blocks never reach that reader, so they are not
/// in the export. Tool results and the turn-finished lines are left out: they are long and add nothing a reader needs.
/// </summary>
public static class TranscriptExport
{
    /// <summary>
    /// The Markdown of one session: a title, the date, the folder and the model; then Claude's recap, when there is one; then the turns.
    /// A turn is "## You" (the owner's text) or "## Claude" (the answer). A tool call is a one-line summary under the answer.
    /// </summary>
    public static string Markdown(IEnumerable<string> lines, string title, DateTimeOffset? date, string? folder, string? model, string? recap)
    {
        var text = new StringBuilder();
        text.Append("# ").Append(title.Trim()).Append("\n\n");
        if (date is { } when) text.Append("- Date: ").Append(when.ToLocalTime().ToString("yyyy-MM-dd HH:mm")).Append('\n');
        if (!string.IsNullOrWhiteSpace(folder)) text.Append("- Project folder: ").Append(folder).Append('\n');
        if (!string.IsNullOrWhiteSpace(model)) text.Append("- Model: ").Append(model).Append('\n');
        text.Append('\n');

        if (!string.IsNullOrWhiteSpace(recap))
            text.Append("## Recap\n\n").Append(recap.Trim()).Append("\n\n");

        var section = string.Empty;
        var number = 0;
        foreach (var line in lines)
        {
            number++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (TranscriptLine.From(number, line) is not { } shown) continue;

            switch (shown.Kind)
            {
                case "you":
                    text.Append("## You\n\n").Append(shown.Text.Trim()).Append("\n\n");
                    section = "you";
                    break;

                case "claude":
                    if (section != "claude") text.Append("## Claude\n\n");
                    text.Append(shown.Text.Trim()).Append("\n\n");
                    section = "claude";
                    break;

                case "tool":
                    if (section != "claude") text.Append("## Claude\n\n");
                    text.Append("- ").Append(shown.Text.Trim()).Append('\n');
                    section = "claude";
                    break;

                case "compact":
                    text.Append("*Conversation compacted here.*\n\n");
                    section = "compact";
                    break;
            }
        }
        return text.ToString().TrimEnd() + "\n";
    }
}
