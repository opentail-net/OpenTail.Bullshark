using System.Globalization;

namespace OpenTail.Bullshark.Core.Tests.Setup;

/// <summary>
/// Synthetic transcript lines for the limit wake tests (limit wake, 2026-11-20). They have the shape the owner's transcripts have (checked
/// 2026-11-18, LimitReset.cs), but no text of any real transcript: every value is made up here.
/// </summary>
internal static class LimitLines
{
    /// <summary>A usage-limit entry, written at <paramref name="hit"/>, that resets at <paramref name="resets"/>.</summary>
    public static string Limit(DateTimeOffset hit, DateTimeOffset resets, string status = "rejected", string type = "five_hour") =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + Stamp(hit) + "\",\"apiError\":\"usage_limit_reached\"," +
        "\"apiErrorParams\":{\"rate_limit_info\":{\"status\":\"" + status + "\",\"resetsAt\":" + resets.ToUnixTimeSeconds() +
        ",\"rateLimitType\":\"" + type + "\"}},\"message\":{\"model\":\"<synthetic>\",\"content\":[{\"type\":\"text\",\"text\":\"synthetic limit\"}]}}";

    /// <summary>An assistant message with a text block and usage, at <paramref name="at"/>. It ends a stop.</summary>
    public static string Assistant(DateTimeOffset at, string id = "msg-1") =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + Stamp(at) + "\",\"requestId\":\"req-1\",\"message\":{\"id\":\"" + id +
        "\",\"model\":\"synthetic-model\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"synthetic reply\"}]," +
        "\"usage\":{\"input_tokens\":3,\"output_tokens\":2,\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":0}}}";

    /// <summary>A typed user message (not a tool result) at <paramref name="at"/>. It ends a stop, as a wake does.</summary>
    public static string UserMessage(DateTimeOffset at) =>
        "{\"type\":\"user\",\"timestamp\":\"" + Stamp(at) + "\",\"message\":{\"role\":\"user\",\"content\":\"synthetic prompt\"}}";

    /// <summary>A user record that carries a tool result. It does not end a stop.</summary>
    public static string ToolResult(DateTimeOffset at) =>
        "{\"type\":\"user\",\"timestamp\":\"" + Stamp(at) + "\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\"," +
        "\"tool_use_id\":\"tool-1\",\"content\":\"synthetic output\"}]}}";

    /// <summary>A metadata record of the kinds that follow a stop. It does not end a stop.</summary>
    public static string Metadata(string type, DateTimeOffset at) =>
        "{\"type\":\"" + type + "\",\"timestamp\":\"" + Stamp(at) + "\",\"sessionId\":\"synthetic\"}";

    /// <summary>The timestamp as the transcripts write it: ISO, UTC, with milliseconds.</summary>
    public static string Stamp(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
