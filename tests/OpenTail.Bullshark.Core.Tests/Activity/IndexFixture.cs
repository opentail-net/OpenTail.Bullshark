using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>Helpers for the index tests: transcript lines, and a description of an index that two builds can be compared by.</summary>
internal static class IndexFixture
{
    /// <summary>An assistant record with a text block, a usage block, and an optional message key (no request id gives no key).</summary>
    public static string Assistant(string messageId, string? requestId, long output, string timestamp, string model)
    {
        var request = requestId is null ? string.Empty : "\"requestId\":\"" + requestId + "\",";
        return "{\"type\":\"assistant\",\"timestamp\":\"" + timestamp + "\"," + request +
               "\"message\":{\"id\":\"" + messageId + "\",\"model\":\"" + model + "\",\"role\":\"assistant\",\"content\":[" +
               "{\"type\":\"text\",\"text\":\"answer " + messageId + "\"}],\"usage\":{\"input_tokens\":4,\"output_tokens\":" + output +
               ",\"cache_creation_input_tokens\":2,\"cache_read_input_tokens\":" + output * 10 + "}}}";
    }

    /// <summary>Every field of an index, in order, so two indexes can be compared as text.</summary>
    public static string Describe(TranscriptIndex index)
    {
        var lines = new List<string> { $"total {index.Totals.Total} counted {index.Totals.CountedMessages}" };
        lines.AddRange(index.Totals.ByModel.Select(pair => $"model {pair.Key} {pair.Value}"));
        foreach (var s in index.Sessions)
        {
            lines.Add(string.Join("|",
                Path.GetFileName(s.FilePath), s.SessionId, s.ProjectFolder, s.IsSubagent, s.FirstActivity, s.LastActivity,
                s.Turns, s.UserMessages, s.AssistantMessages, s.ToolCalls,
                string.Join(",", s.ToolNames.Select(pair => $"{pair.Key}={pair.Value}")),
                string.Join(",", s.FilesWritten), s.LastAssistantText, s.LastModel, s.ContextTokensAtLastTurn,
                s.Usage, s.FirstUserText, s.AiTitle, s.CustomTitle, s.LastRecap, s.LastRecapAt, s.Compactions,
                s.ContinuesSessionId, s.FailedToolCalls));
        }
        lines.AddRange(index.Skipped.Select(skip => $"skipped {Path.GetFileName(skip.FilePath)}: {skip.Reason}"));
        return string.Join("\n", lines);
    }
}
