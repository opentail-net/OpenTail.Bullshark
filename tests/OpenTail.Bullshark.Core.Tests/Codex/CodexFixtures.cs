using System.IO.Compression;
using System.Text;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Tests.Codex;

/// <summary>
/// Synthetic Codex rollouts for the tests (stage C6). Every id, path and text here is made up; no real transcript is read, and the owner's
/// ~/.codex is never passed to the code under test. A test owns one temp home folder and deletes it afterwards.
/// </summary>
internal sealed class CodexHome : IDisposable
{
    public const string SessionId = "019e1000-0000-7000-8000-000000000001";
    public const string SecondId = "019e1000-0000-7000-8000-000000000002";
    public const string NoIdUuid = "019e1000-0000-7000-8000-000000000003";

    public const string FirstMessage = "Fix the parser";

    /// <summary>The temp folder used as CODEX_HOME. Created on construction and deleted by <see cref="Dispose"/>.</summary>
    public string Home { get; } = Path.Combine(Path.GetTempPath(), "bullshark-codex-" + Guid.NewGuid().ToString("N"));

    public string Sessions => Path.Combine(Home, "sessions");

    public CodexHome() => Directory.CreateDirectory(Home);

    /// <summary>The provider paths of this home: CODEX_HOME is set to the temp folder, and no user profile is read.</summary>
    public ProviderPaths Paths() => ProviderPaths.ForCodex(
        name => name == ProviderPaths.CodexHomeVariable ? Home : null,
        userProfile: Path.Combine(Home, "no-profile"));

    /// <summary>The rollout file name for a session id: a timestamp and the id, as Codex writes them.</summary>
    public static string NameFor(string id) => "rollout-2026-11-20T10-00-00-" + id + ".jsonl";

    /// <summary>A UUIDv7 text (version nibble 7) whose first 48 bits are <paramref name="unixMillis"/>. Synthetic: the time and the tail are made up.</summary>
    public static string UuidV7(long unixMillis, string tail = "000000000001")
    {
        var hex = unixMillis.ToString("x12");
        return hex[..8] + "-" + hex[8..] + "-7000-8000-" + tail;
    }

    /// <summary>The path under sessions for a file name (the folder layout YYYY\MM\DD that Codex uses).</summary>
    public string PathFor(string fileName) => PathOn(new DateOnly(2026, 11, 20), fileName);

    /// <summary>The path under sessions for a file name in the dated folder of <paramref name="day"/> (YYYY\MM\DD, zero-padded).</summary>
    public string PathOn(DateOnly day, string fileName) => Path.Combine(
        Sessions, day.ToString("yyyy", System.Globalization.CultureInfo.InvariantCulture),
        day.ToString("MM", System.Globalization.CultureInfo.InvariantCulture),
        day.ToString("dd", System.Globalization.CultureInfo.InvariantCulture), fileName);

    /// <summary>Writes plain lines as a rollout file.</summary>
    public string WritePlain(string fileName, IEnumerable<string> lines)
    {
        var path = PathFor(fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
        return path;
    }

    /// <summary>Writes plain lines as a rollout file in the dated folder of <paramref name="day"/>.</summary>
    public string WritePlainOn(DateOnly day, string fileName, IEnumerable<string> lines)
    {
        var path = PathOn(day, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
        return path;
    }

    /// <summary>
    /// Writes lines as a zstd-compressed rollout. <c>.zst</c> is added to the file name, so the reader sees the compressed name; pass the plain
    /// rollout name (<see cref="NameFor"/>).
    /// </summary>
    public string WriteCompressed(string plainFileName, IEnumerable<string> lines)
    {
        var path = PathFor(plainFileName + ".zst");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
        using var file = File.Create(path);
        using (var zstd = CompressingStream(file))
        {
            zstd.Write(bytes);
        }
        return path;
    }

    /// <summary>
    /// A stream that compresses what is written to <paramref name="destination"/> as zstd: the BCL's <c>ZstandardStream</c> on net11.0, and
    /// ZstdSharp's <c>CompressionStream</c> (from ZstdSharp.Port, the package the reader uses) on net10.0. Dispose it before the destination is
    /// read, so the last frame is written.
    /// </summary>
    public static Stream CompressingStream(Stream destination)
    {
#if NET11_0_OR_GREATER
        return new ZstandardStream(destination, CompressionMode.Compress);
#else
        return new ZstdSharp.CompressionStream(destination);
#endif
    }

    /// <summary>Writes the session index (names only) in the home folder.</summary>
    public void WriteIndex(params string[] lines) => File.WriteAllLines(Path.Combine(Home, "session_index.jsonl"), lines);

    public void Dispose()
    {
        if (!Directory.Exists(Home)) return;
        try { Directory.Delete(Home, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>One record line: its timestamp, type and payload (a JSON object text).</summary>
    public static string Record(string timestamp, string type, string payload) =>
        "{\"timestamp\":\"" + timestamp + "\",\"type\":\"" + type + "\",\"payload\":" + payload + "}";

    /// <summary>A session record. A null id writes none (the test for the file-name id).</summary>
    public static string Meta(string? id) => Record(
        "2026-11-20T10:00:00.000Z",
        "session_meta",
        "{" + (id is null ? string.Empty : "\"id\":\"" + id + "\",") + "\"cwd\":\"C:\\\\work\\\\demo\",\"source\":\"cli\"}");

    /// <summary>A turn record: the model and the folder of the turn.</summary>
    public static string Turn() => Record(
        "2026-11-20T10:00:01.000Z",
        "turn_context",
        "{\"turn_id\":\"t1\",\"cwd\":\"C:\\\\work\\\\demo\",\"model\":\"gpt-test-1\",\"effort\":\"medium\"}");

    /// <summary>A user or assistant message as a response record, with its text.</summary>
    public static string Message(string timestamp, string role, string text)
    {
        var part = role == "user" ? "input_text" : "output_text";
        return Record(timestamp, "response_item",
            "{\"type\":\"message\",\"role\":\"" + role + "\",\"content\":[{\"type\":\"" + part + "\",\"text\":\"" + text + "\"}]}");
    }

    /// <summary>
    /// The standard synthetic rollout: a session record, a turn, two user messages and one assistant message, a record of a type the reader
    /// does not know, and one line that is not JSON. It has seven lines.
    /// </summary>
    public static string[] Standard(string? id) =>
    [
        Meta(id),
        Turn(),
        Message("2026-11-20T10:00:02.000Z", "user", FirstMessage),
        Record("2026-11-20T10:00:03.000Z", "future_record_type", "{\"x\":1}"),
        Message("2026-11-20T10:00:04.000Z", "assistant", "Done"),
        "this is not json",
        Message("2026-11-20T10:00:06.000Z", "user", "Second question"),
    ];
}
