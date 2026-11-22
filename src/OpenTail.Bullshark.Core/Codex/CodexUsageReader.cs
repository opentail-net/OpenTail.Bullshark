using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// One Codex session file's usage (R8): the last valid running total in the file, the model in effect at that total, the project (the last
/// segment of the session's folder) and the times of the first and last valid total. One record per file; the figures are counts and times only.
/// </summary>
/// <param name="Path">The rollout file. It is the record's key in the usage report and is never written anywhere.</param>
/// <param name="Project">The last segment of the session's folder, or <see cref="CodexUsageReader.NoProject"/>.</param>
/// <param name="Model">The model of the last <c>turn_context</c> before the last valid total, or <c>unknown</c>.</param>
/// <param name="Usage">The total as <see cref="TokenUsage"/>: uncached input, output, cache written and cache read (see <see cref="CodexUsageReader"/>).</param>
/// <param name="FirstTotal">The time of the first valid total in the file.</param>
/// <param name="LastTotal">The time of the last valid total in the file.</param>
public sealed record CodexSessionUsage(string Path, string Project, string Model, TokenUsage Usage, DateTimeOffset FirstTotal, DateTimeOffset LastTotal);

/// <summary>
/// Reads the token usage of Codex rollouts (R8). A session file gives one record: its last valid running total (a <c>event_msg</c> record with
/// <c>payload.type</c> <c>token_count</c> whose <c>payload.info.total_token_usage</c> is an object with the figures). The totals are cumulative, so
/// the last one is the session's total and the events are never summed. Verified on the owner's rollouts (see the R8 plan): each file starts from
/// zero, a repeated event repeats the total, and <c>total_tokens</c> is <c>input_tokens</c> + <c>output_tokens</c>.
/// <para>
/// The mapping to <see cref="TokenUsage"/>: <c>cached_input_tokens</c> sits inside <c>input_tokens</c>, so the uncached part is counted once as
/// <c>Input</c> and the cached part as <c>CacheRead</c>. <c>cache_write_input_tokens</c> is taken the same way (as <c>CacheCreation</c>, inside
/// input: an assumption, always zero on the owner's data). <c>reasoning_output_tokens</c> sits inside <c>output_tokens</c>: <c>Output</c> is
/// <c>output_tokens</c> when reasoning counts as output, and <c>output_tokens</c> less the reasoning when it does not.
/// </para>
/// <para>
/// A line that does not parse is skipped, so a partly written last line of a live session is left out and the rest counts. A token-count event
/// with no figures (no <c>info</c>, no total, or a figure that is not a non-negative whole number) is skipped. A file with no valid total gives no
/// record. Lines are read with <see cref="RolloutReader"/>, which decodes compressed rollouts on net11.0 only.
/// </para>
/// </summary>
public static class CodexUsageReader
{
    /// <summary>The project of a session whose folder is not named by its records.</summary>
    public const string NoProject = "(codex)";

    /// <summary>The model of a total that no <c>turn_context</c> before it names (the same word the Claude reader uses).</summary>
    private const string UnknownModel = "unknown";

    /// <summary>The usage of one rollout's lines: the last valid total and what it carries. Pure over the lines; <paramref name="path"/> is only the key.</summary>
    /// <param name="path">The rollout file the lines came from (the record's key).</param>
    /// <param name="lines">The file's lines in order. A line that is not a JSON object is skipped.</param>
    /// <param name="reasoningAsOutput">The <c>usage.reasoningAsOutput</c> setting: true puts reasoning in output, false takes it out.</param>
    /// <returns>The file's record, or null when no line gives a valid total.</returns>
    public static CodexSessionUsage? FromLines(string path, IEnumerable<string> lines, bool reasoningAsOutput)
    {
        string? model = null;
        string? sessionFolder = null;
        string? turnFolder = null;
        DateTimeOffset? lastSeen = null;
        DateTimeOffset? firstTotal = null;
        TokenUsage? lastUsage = null;
        string lastModel = UnknownModel;
        DateTimeOffset lastAt = default;

        foreach (var line in lines)
        {
            using var document = RolloutReader.TryParseObject(line);
            if (document is null) continue;
            var root = document.RootElement;
            if (RolloutReader.TimestampOf(root) is { } own) lastSeen = own;

            var payload = root.TryGetProperty("payload", out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
            switch (RolloutReader.StringOf(root, "type"))
            {
                case "session_meta":
                    sessionFolder ??= RolloutReader.StringOf(payload, "cwd");
                    break;

                case "turn_context":
                    model = RolloutReader.StringOf(payload, "model") ?? model;
                    turnFolder = RolloutReader.StringOf(payload, "cwd") ?? turnFolder;
                    break;

                case "event_msg":
                    if (RolloutReader.StringOf(payload, "type") != "token_count" || lastSeen is not { } at) break;
                    if (UsageOf(payload, reasoningAsOutput) is not { } usage) break;
                    firstTotal ??= at;
                    lastUsage = usage;
                    lastModel = model ?? UnknownModel;
                    lastAt = at;
                    break;
            }
        }

        if (lastUsage is not { } total || firstTotal is not { } first) return null;
        return new CodexSessionUsage(path, ProjectOf(sessionFolder ?? turnFolder), lastModel, total, first, lastAt);
    }

    /// <summary>
    /// The record of one rollout file, or null when the file has no valid total or cannot be read (locked, removed, denied, or compressed on a
    /// build that cannot decode it). A file that is being written is read to its last complete line.
    /// </summary>
    public static CodexSessionUsage? ReadFile(string path, bool reasoningAsOutput)
    {
        try
        {
            using var reader = RolloutReader.Open(path);
            return FromLines(path, Texts(RolloutReader.Lines(reader, CancellationToken.None)), reasoningAsOutput);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The records of the rollouts under <paramref name="historyRoot"/> that were last written at or after <paramref name="since"/> (a rollout
    /// last written earlier has no record in the window under any attribution rule, so it is not read). One record per file with a valid total,
    /// in path order. Compressed rollouts are skipped, and counted in <paramref name="log"/>, on a build that cannot decode them. A missing folder
    /// gives an empty list.
    /// </summary>
    public static IReadOnlyList<CodexSessionUsage> ReadFolder(string historyRoot, DateTimeOffset since, bool reasoningAsOutput, Action<string>? log = null)
    {
        if (!Directory.Exists(historyRoot)) return [];
        var sinceUtc = since.UtcDateTime;
        var found = new List<CodexSessionUsage>();
        var compressedSkipped = 0;
        foreach (var path in RolloutReader.Files(historyRoot))
        {
            DateTime written;
            try
            {
                written = new FileInfo(path).LastWriteTimeUtc;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            if (written < sinceUtc) continue;
            if (path.EndsWith(".zst", StringComparison.OrdinalIgnoreCase) && !RolloutReader.CanDecodeCompressed)
            {
                compressedSkipped++;
                continue;
            }
            if (ReadFile(path, reasoningAsOutput) is { } usage) found.Add(usage);
        }
        if (compressedSkipped > 0)
        {
            log?.Invoke($"codex usage: {compressedSkipped} compressed rollout(s) not read by this build (net11.0 decodes them)");
        }
        return found;
    }

    /// <summary>The figures of one token-count payload as a <see cref="TokenUsage"/>, or null when it has no valid total.</summary>
    private static TokenUsage? UsageOf(JsonElement payload, bool reasoningAsOutput)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object) return null;
        if (!info.TryGetProperty("total_token_usage", out var total) || total.ValueKind != JsonValueKind.Object) return null;
        if (!TryCount(total, "input_tokens", required: true, out var input)) return null;
        if (!TryCount(total, "output_tokens", required: true, out var output)) return null;
        if (!TryCount(total, "cached_input_tokens", required: false, out var cached)) return null;
        if (!TryCount(total, "cache_write_input_tokens", required: false, out var write)) return null;
        if (!TryCount(total, "reasoning_output_tokens", required: false, out var reasoning)) return null;

        // Cached input and cache written are inside input (verified on the owner's data for cached input); never counted twice. The clamp only
        // matters for a figure that cannot happen; it keeps the count from going negative.
        var uncached = Math.Max(0, input - cached - write);
        var counted = reasoningAsOutput ? output : Math.Max(0, output - reasoning);
        return new TokenUsage(uncached, counted, write, cached);
    }

    /// <summary>
    /// A figure of a total: a non-negative whole number. A missing figure is zero when it is optional, and makes the total invalid when it is required.
    /// A figure that is present but is not a non-negative whole number makes the total invalid.
    /// </summary>
    private static bool TryCount(JsonElement total, string name, bool required, out long value)
    {
        value = 0;
        if (!total.TryGetProperty(name, out var element)) return !required;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value) && value >= 0;
    }

    /// <summary>The last segment of a session's folder, as a project name: <see cref="NoProject"/> when there is none, or when it is a drive root.</summary>
    private static string ProjectOf(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return NoProject;
        var segments = folder.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var name = segments.Length > 0 ? segments[^1] : string.Empty;
        return name.Length == 0 || name.EndsWith(':') ? NoProject : name;
    }

    /// <summary>The text of each line, with a line too long to be read (<see cref="RolloutReader.RolloutLine.Overlong"/>) as empty, which is not JSON.</summary>
    private static IEnumerable<string> Texts(IEnumerable<RolloutReader.RolloutLine> lines)
    {
        foreach (var line in lines) yield return line.Overlong ? string.Empty : line.Text;
    }
}
