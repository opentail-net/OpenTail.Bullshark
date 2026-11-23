using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenTail.Bullshark.Core.Processes;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>One model the account is offered: its id (what <c>-m</c> takes), its display name, and whether it is the default.</summary>
public sealed record CodexModelEntry(string Id, string DisplayName, bool IsDefault);

/// <summary>Why the model list could not be read. Only this type is shown to the owner, never the text of the failure.</summary>
public enum CodexModelProblem
{
    /// <summary>The list was read and has at least one model.</summary>
    None,

    /// <summary>The <c>cmd.exe</c> process could not be started.</summary>
    NotStarted,

    /// <summary>The process ended before the reply to the model list came (for example, <c>codex</c> is not installed).</summary>
    ExitedWithoutReply,

    /// <summary>No reply came within the time allowed, so the process tree was stopped.</summary>
    TimedOut,

    /// <summary>The app-server answered with an error.</summary>
    AppServerError,

    /// <summary>A reply was not the expected JSON shape.</summary>
    BadReply,

    /// <summary>The reply had no model that could be offered (every entry hidden or with an id that fails the check).</summary>
    NoModels,

    /// <summary>Reading failed in a way that is not one of the above (the reader threw). The type of the exception is logged by the App.</summary>
    Unexpected,
}

/// <summary>
/// The result of reading the model list. <see cref="Models"/> holds the visible entries whose ids pass <see cref="CodexModelList.IsValidModelId"/>, with
/// exactly one marked as the default. <see cref="Hidden"/> counts the entries the account does not offer (<c>hidden: true</c>); <see cref="Dropped"/>
/// counts the other entries that were not usable (an id that fails the check, or an entry that is not an object). <see cref="Problem"/> is
/// <see cref="CodexModelProblem.None"/> only when <see cref="Models"/> has at least one entry.
/// </summary>
public sealed record CodexModelReading(IReadOnlyList<CodexModelEntry> Models, int Hidden, int Dropped, CodexModelProblem Problem);

/// <summary>
/// Reads the models the account is offered, from <c>codex app-server</c> (decisions M-D1 and M-D2 of the Codex provider plan, D3). The conversation is
/// the one the evidence probe used: <c>initialize</c>, then the <c>initialized</c> notification and <c>model/list</c> with <c>includeHidden</c> and a limit of
/// 100. The reply is read line by line until the one with the <c>model/list</c> id. No model turn is started, and the process tree is stopped when the
/// reply is in, or at the timeout. The program and its arguments are settable so a test can use a fake server; the default runs <c>codex app-server</c>
/// through <c>cmd.exe</c>, because <c>codex</c> is a <c>.cmd</c> shim on this machine (as in <see cref="CodexTerminalOpener"/>).
/// </summary>
public sealed class CodexModelList(string program = "cmd.exe", IReadOnlyList<string>? arguments = null, TimeSpan? timeout = null)
{
    /// <summary>How long the whole read may take before the process tree is stopped (M-D1).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    /// <summary>A model id as <c>-m</c> takes it (decision D3): lower-case letters, digits, dots and hyphens, starting with a letter or digit.</summary>
    private static readonly Regex ModelIdShape = new(@"\A[a-z0-9][a-z0-9.-]*\z", RegexOptions.CultureInvariant);

    /// <summary>The version sent to the app-server as the client's version (the Core assembly's version).</summary>
    private static readonly string ClientVersion = typeof(CodexModelList).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private static readonly string[] DefaultArguments = ["/d", "/c", "codex", "app-server"];

    private readonly string _program = program;
    private readonly IReadOnlyList<string> _arguments = arguments ?? DefaultArguments;
    private readonly TimeSpan _timeout = timeout ?? DefaultTimeout;

    /// <summary>True when <paramref name="id"/> is a model id that may be passed as <c>-m</c>: the pattern is <c>[a-z0-9][a-z0-9.-]*</c>.</summary>
    public static bool IsValidModelId(string? id) => id is not null && ModelIdShape.IsMatch(id);

    /// <summary>
    /// Reads the model list from a <c>model/list</c> reply (the whole JSON line of that reply). Hidden entries are not offered and are counted; an entry
    /// whose id fails <see cref="IsValidModelId"/> is dropped and counted. The default is the first offered entry with <c>isDefault</c>; when none has it,
    /// the first offered entry is the default. A line that is not JSON, has no <c>result.data</c> array, or is an error reply gives a failure, never a throw.
    /// </summary>
    public static CodexModelReading Parse(string jsonLine)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(jsonLine);
        }
        catch (JsonException)
        {
            return Failed(CodexModelProblem.BadReply);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Failed(CodexModelProblem.BadReply);
            if (root.TryGetProperty("error", out _)) return Failed(CodexModelProblem.AppServerError);
            if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object
                || !result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return Failed(CodexModelProblem.BadReply);

            var offered = new List<(string Id, string Name, bool Flagged)>();
            var hidden = 0;
            var dropped = 0;
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    dropped++;
                    continue;
                }
                if (item.TryGetProperty("hidden", out var hiddenValue) && hiddenValue.ValueKind == JsonValueKind.True)
                {
                    hidden++;
                    continue;
                }
                var id = item.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String ? idValue.GetString() : null;
                if (!IsValidModelId(id))
                {
                    dropped++;
                    continue;
                }
                var name = item.TryGetProperty("displayName", out var nameValue) && nameValue.ValueKind == JsonValueKind.String ? nameValue.GetString() : null;
                var flagged = item.TryGetProperty("isDefault", out var defaultValue) && defaultValue.ValueKind == JsonValueKind.True;
                offered.Add((id!, string.IsNullOrWhiteSpace(name) ? id! : name, flagged));
            }

            if (offered.Count == 0) return new CodexModelReading([], hidden, dropped, CodexModelProblem.NoModels);

            var defaultIndex = offered.FindIndex(entry => entry.Flagged);
            if (defaultIndex < 0) defaultIndex = 0;
            var models = offered.Select((entry, index) => new CodexModelEntry(entry.Id, entry.Name, index == defaultIndex)).ToList();
            return new CodexModelReading(models, hidden, dropped, CodexModelProblem.None);
        }
    }

    /// <summary>
    /// Runs the conversation and returns the reading. A failure to start, an exit before the reply, a timeout or a bad reply is a
    /// <see cref="CodexModelProblem"/>, never a throw. A cancellation by <paramref name="ct"/> is thrown to the caller. The process tree is stopped in
    /// every case.
    /// </summary>
    public async Task<CodexModelReading> ReadAsync(CancellationToken ct)
    {
        Process process;
        try
        {
            process = Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return Failed(CodexModelProblem.NotStarted);
        }

        using (process)
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            limit.CancelAfter(_timeout);
            try
            {
                return await Converse(process, limit.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Failed(CodexModelProblem.TimedOut);
            }
            finally
            {
                StopTree(process);
            }
        }
    }

    /// <summary>The conversation: initialize and its reply, then the initialized notification and model/list, then the reply to model/list.</summary>
    private static async Task<CodexModelReading> Converse(Process process, CancellationToken ct)
    {
        // The error output is read and dropped, so the process cannot block on a full pipe. It is never logged.
        _ = DiscardAsync(process.StandardError);

        await SendAsync(process, InitializeLine(), ct);
        if (await ReadReplyAsync(process, 1, ct) is null) return Failed(CodexModelProblem.ExitedWithoutReply);

        await SendAsync(process, InitializedLine, ct);
        await SendAsync(process, ModelListLine, ct);
        var reply = await ReadReplyAsync(process, 2, ct);
        return reply is null ? Failed(CodexModelProblem.ExitedWithoutReply) : Parse(reply);
    }

    private Process Start()
    {
        var info = ProcessLaunch.Create(
            _program,
            _arguments,
            redirectStandardInput: true,
            outputEncoding: Encoding.UTF8,
            inputEncoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return Process.Start(info) ?? throw new InvalidOperationException("The process did not start.");
    }

    /// <summary>
    /// Writes one line to standard input. A write that fails because the process stopped reading is not an error here: the reply is then read from the
    /// output, and a missing reply is reported as an exit.
    /// </summary>
    private static async Task SendAsync(Process process, string line, CancellationToken ct)
    {
        try
        {
            await process.StandardInput.WriteLineAsync(line.AsMemory(), ct);
        }
        catch (IOException)
        {
            // The process stopped reading: its output says why.
        }
    }

    /// <summary>The next line of standard output that is the reply to <paramref name="id"/>, or null when the output ends first.</summary>
    private static async Task<string?> ReadReplyAsync(Process process, int id, CancellationToken ct)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(ct);
            if (line is null) return null;
            if (IsReplyTo(line, id)) return line;
        }
    }

    /// <summary>True when the line is a JSON reply (it has an id equal to <paramref name="id"/> and no method) to that request. Other lines are skipped.</summary>
    private static bool IsReplyTo(string line, int id)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && !root.TryGetProperty("method", out _)
                && root.TryGetProperty("id", out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var number)
                && number == id;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task DiscardAsync(StreamReader reader)
    {
        try
        {
            await reader.ReadToEndAsync();
        }
        catch (IOException)
        {
            // The process was stopped while its error output was being read.
        }
        catch (ObjectDisposedException)
        {
            // The process was disposed first.
        }
    }

    /// <summary>Stops the process and whatever it started (the <c>cmd.exe</c> and the Codex it runs). A process that has already exited is left as it is.</summary>
    private static void StopTree(Process process) => ProcessLaunch.Kill(process);

    private static string InitializeLine() =>
        "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"opentail-bullshark\",\"version\":\"" +
        ClientVersion + "\"}}}";

    private const string InitializedLine = """{"jsonrpc":"2.0","method":"initialized"}""";

    private const string ModelListLine = """{"jsonrpc":"2.0","id":2,"method":"model/list","params":{"includeHidden":true,"limit":100}}""";

    private static CodexModelReading Failed(CodexModelProblem problem) => new([], 0, 0, problem);
}
