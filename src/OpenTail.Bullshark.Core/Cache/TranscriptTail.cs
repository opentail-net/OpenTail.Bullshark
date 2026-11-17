using System.Text;
using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Cache;

/// <summary>
/// What the end of a transcript says about its cache: the time of the last request (the last assistant message with
/// usage), the lifetime of the cache that request used, and the context it carried.
/// </summary>
public sealed record TailFacts(DateTimeOffset? LastRequest, CacheTtl Ttl, long ContextTokens);

/// <summary>
/// Reads the end of a transcript without reading the whole file. The window starts at 64 KB and doubles up to 16 MB,
/// because one tool result can be larger than the window. The file is opened for shared reading, writing and deleting,
/// since the session may be writing it now. The partial first line of a window, and a last line that does not parse yet,
/// are skipped.
/// </summary>
public static class TranscriptTail
{
    public const int StartWindow = 64 * 1024;
    public const int MaxWindow = 16 * 1024 * 1024;

    private sealed record Request(DateTimeOffset? Time, long Input, long CacheRead, long CacheWrite, long Write1h, long Write5m)
    {
        public long Context => Input + CacheRead + CacheWrite;

        /// <summary>The lifetime this request wrote with, or null when it wrote nothing that shows one.</summary>
        public CacheTtl? Ttl => Write1h > 0 ? CacheTtl.OneHour : Write5m > 0 ? CacheTtl.FiveMinutes : null;
    }

    /// <summary>Reads the cache facts of a transcript. Returns no request when the file holds no usage record.</summary>
    public static TailFacts? Read(string path)
    {
        using var stream = TranscriptFile.Open(path);
        long size = stream.Length;
        int window = StartWindow;

        while (true)
        {
            int take = (int)Math.Min(window, size);
            var bytes = new byte[take];
            stream.Seek(size - take, SeekOrigin.Begin);
            int filled = 0;
            while (filled < take)
            {
                int read = stream.Read(bytes, filled, take - filled);
                if (read == 0) break;
                filled += read;
            }

            var lines = Encoding.UTF8.GetString(bytes, 0, filled).Split('\n');
            // The first line of a window that does not start the file is cut in the middle, so it is skipped.
            int firstUsable = take < size ? 1 : 0;
            var facts = Scan(lines, firstUsable);
            if (facts is not null) return facts;

            if (take >= size || window >= MaxWindow) return null;
            window = (int)Math.Min((long)window * 2, MaxWindow);
        }
    }

    private static TailFacts? Scan(string[] lines, int firstUsable)
    {
        Request? last = null;
        for (int i = lines.Length - 1; i >= firstUsable; i--)
        {
            var request = Parse(lines[i]);
            if (request is null) continue;
            last ??= request;

            // The lifetime is taken from the most recent request that wrote the cache. The last request may only have read it.
            var ttl = request.Ttl;
            if (ttl is not null) return new TailFacts(last.Time, ttl.Value, last.Context);
        }
        return last is null ? null : new TailFacts(last.Time, CacheTtl.Unknown, last.Context);
    }

    private static Request? Parse(string line)
    {
        // A line that does not parse yet (the session may be writing it) is not a request. Nor is one whose values cannot be read (round 12 item A1).
        if (!TranscriptJson.TryParse(line, out var document)) return null;

        using (document)
        {
            try
            {
                return RequestOf(document.RootElement);
            }
            catch (Exception ex) when (TranscriptJson.IsUnreadable(ex))
            {
                return null;
            }
        }
    }

    private static Request? RequestOf(JsonElement root)
    {
        if (TranscriptJson.Str(root, "type") != "assistant") return null;
        if (!root.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("usage", out var usage) ||
            usage.ValueKind != JsonValueKind.Object)
            return null;

        var time = TranscriptJson.Time(root, "timestamp");

        long write1h = 0, write5m = 0;
        if (usage.TryGetProperty("cache_creation", out var split) && split.ValueKind == JsonValueKind.Object)
        {
            write1h = TranscriptJson.Long(split, "ephemeral_1h_input_tokens");
            write5m = TranscriptJson.Long(split, "ephemeral_5m_input_tokens");
        }

        return new Request(time,
            TranscriptJson.Long(usage, "input_tokens"),
            TranscriptJson.Long(usage, "cache_read_input_tokens"),
            TranscriptJson.Long(usage, "cache_creation_input_tokens"),
            write1h,
            write5m);
    }
}
