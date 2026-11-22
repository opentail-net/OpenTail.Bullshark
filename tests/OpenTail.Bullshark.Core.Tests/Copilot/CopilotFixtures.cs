using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Tests.Copilot;

/// <summary>
/// Synthetic Copilot session folders for the tests (G1). Every id, path, name and text here is made up: the event and key names are the ones the
/// research note records (the envelope <c>type, data, id, timestamp, parentId</c>, and the data keys of each event), and no real session is read.
/// A test owns one temp home folder; <see cref="Paths"/> points the code under test at it, so the owner's <c>~\.copilot</c> is never read.
/// </summary>
internal sealed class CopilotHome : IDisposable
{
    public const string SessionId = "3f2a9c10-1b2d-4e5f-8a9b-0c1d2e3f4a51";
    public const string SecondId = "3f2a9c10-1b2d-4e5f-8a9b-0c1d2e3f4a52";
    public const string EmptyId = "3f2a9c10-1b2d-4e5f-8a9b-0c1d2e3f4a53";
    public const string MissingId = "3f2a9c10-1b2d-4e5f-8a9b-0c1d2e3f4a54";
    public const string FirstMessage = "Fix the parser";

    /// <summary>The folder of the fake Copilot home (COPILOT_HOME). Created on construction and deleted by <see cref="Dispose"/>.</summary>
    public string Home { get; } = Path.Combine(Path.GetTempPath(), "bullshark-copilot-" + Guid.NewGuid().ToString("N"));

    /// <summary>The <c>session-state</c> folder under the fake home.</summary>
    public string SessionState => Path.Combine(Home, "session-state");

    public CopilotHome() => Directory.CreateDirectory(SessionState);

    /// <summary>The provider paths of this home: COPILOT_HOME is set to the temp folder, and no user profile is read.</summary>
    public ProviderPaths Paths() => ProviderPaths.ForCopilot(
        name => name == ProviderPaths.CopilotHomeVariable ? Home : null,
        userProfile: Path.Combine(Home, "no-profile"));

    /// <summary>The folder of one session.</summary>
    public string Folder(string id) => Path.Combine(SessionState, id);

    /// <summary>Writes a session's <c>workspace.yaml</c> lines and returns the file's path.</summary>
    public string WriteWorkspace(string id, params string[] lines) => WriteFile(id, "workspace.yaml", lines);

    /// <summary>Writes a session's <c>events.jsonl</c> lines and returns the file's path.</summary>
    public string WriteEvents(string id, params string[] lines) => WriteFile(id, "events.jsonl", lines);

    private string WriteFile(string id, string name, string[] lines)
    {
        Directory.CreateDirectory(Folder(id));
        var path = Path.Combine(Folder(id), name);
        File.WriteAllLines(path, lines);
        return path;
    }

    public void Dispose()
    {
        if (!Directory.Exists(Home)) return;
        try { Directory.Delete(Home, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>The text of a JSON string value: backslashes, quotes and the line and tab characters escaped (a raw line break is not valid JSON).</summary>
    public static string Json(string text) => text
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal);

    /// <summary>One event line: the envelope with the given type, timestamp and data (a JSON object text).</summary>
    public static string Event(string type, string timestamp, string data) =>
        "{\"type\":\"" + type + "\",\"data\":" + data + ",\"id\":\"00000000-0000-4000-8000-000000000000\",\"timestamp\":\"" + timestamp + "\",\"parentId\":null}";

    /// <summary>A <c>session.start</c> event. The id is the session id; the context carries the cwd and the git fields.</summary>
    public static string SessionStart(string id, string? cwd = "C:\\Fixture\\Demo", string selectedModel = "fixture-model-a") =>
        Event(
            "session.start",
            "2026-11-20T10:00:00.000Z",
            "{\"sessionId\":\"" + id + "\",\"version\":1,\"producer\":\"fixture\",\"copilotVersion\":\"0.0.0-fixture\"," +
            "\"startTime\":\"2026-11-20T10:00:00.000Z\",\"selectedModel\":\"" + selectedModel + "\"," +
            "\"context\":{" + (cwd is null ? string.Empty : "\"cwd\":\"" + Json(cwd) + "\",") +
            "\"gitRoot\":\"C:\\\\Fixture\\\\Demo\",\"branch\":\"fixture-branch\",\"repository\":\"fixture/demo\",\"hostType\":\"fixture\"}," +
            "\"alreadyInUse\":false,\"remoteSteerable\":false}");

    /// <summary>A <c>session.model_change</c> event: the model the session changed to.</summary>
    public static string ModelChange(string timestamp, string newModel) =>
        Event("session.model_change", timestamp, "{\"newModel\":\"" + newModel + "\"}");

    /// <summary>A <c>user.message</c> event whose <c>content</c> is the text.</summary>
    public static string UserMessage(string timestamp, string content) => Event(
        "user.message",
        timestamp,
        "{\"content\":\"" + Json(content) + "\",\"transformedContent\":\"fixture\",\"attachments\":[],\"interactionId\":\"fixture-1\"}");

    /// <summary>An <c>assistant.message</c> event whose <c>content</c> is the text.</summary>
    public static string AssistantMessage(string timestamp, string content) => Event(
        "assistant.message",
        timestamp,
        "{\"messageId\":\"fixture-m\",\"model\":\"fixture-model-a\",\"content\":\"" + Json(content) + "\",\"toolRequests\":[],\"turnId\":\"0\",\"outputTokens\":1,\"requestId\":\"fixture-r\"}");

    /// <summary>An <c>assistant.turn_end</c> event (the end of a finished turn).</summary>
    public static string TurnEnd(string timestamp) => Event("assistant.turn_end", timestamp, "{\"turnId\":\"0\"}");

    /// <summary>A system event, which the transcript leaves out.</summary>
    public static string SystemMessage(string timestamp) => Event("system.message", timestamp, "{\"content\":\"fixture system text\"}");

    /// <summary>A session event of another kind (a shutdown), which the counts leave out.</summary>
    public static string Shutdown(string timestamp) => Event("session.shutdown", timestamp, "{\"shutdownType\":\"routine\",\"totalPremiumRequests\":0}");

    /// <summary>
    /// The standard synthetic session: a start, a model change, two user messages, two assistant replies with their turn ends, a system event, and a
    /// shutdown. It has two user messages, two assistant messages, two turns, and the model <c>fixture-model-b</c>.
    /// </summary>
    public static string[] Standard(string id) =>
    [
        SessionStart(id),
        ModelChange("2026-11-20T10:00:01.000Z", "fixture-model-b"),
        UserMessage("2026-11-20T10:00:02.000Z", FirstMessage),
        SystemMessage("2026-11-20T10:00:03.000Z"),
        AssistantMessage("2026-11-20T10:00:04.000Z", "Done"),
        TurnEnd("2026-11-20T10:00:04.500Z"),
        UserMessage("2026-11-20T10:00:05.000Z", "Second question"),
        AssistantMessage("2026-11-20T10:00:06.000Z", "Answer two"),
        TurnEnd("2026-11-20T10:00:06.500Z"),
        Shutdown("2026-11-20T10:00:07.000Z"),
    ];

    /// <summary>A workspace file with a name, a cwd and the times. An empty name is written as a blank value.</summary>
    public static string[] Workspace(string id, string name = "Fixture session name", string cwd = "C:\\Fixture\\Demo") =>
    [
        "id: " + id,
        "name: " + name,
        "user_named: false",
        "summary_count: 0",
        "cwd: " + cwd,
        "git_root: C:\\Fixture\\Demo",
        "branch: fixture-branch",
        "repository: fixture/demo",
        "host_type: fixture",
        "created_at: 2026-11-20T10:00:00.000Z",
        "updated_at: 2026-11-20T10:05:00.000Z",
    ];
}
