using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Cache;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Trust;

// OpenTail.Bullshark supervisor: keeps the sessions of every run in runs.json alive, headless.
//
//   OpenTail.Bullshark.Supervisor [--state <state.json>] [--interval 30] [--once] [--workdir <folder>] [--task "<task>"]
//
// Each pass reads `claude agents --json`, starts what is missing, restarts what is gone, alerts on what is blocked or
// failed, and warns before the login expires. A run whose folder is untrusted waits; the other runs go on.
// With no run yet, nothing is started: start a run from the OpenTail.Bullshark window (setup page).
// The sessions talk to each other directly with SendMessage; this process does not carry messages (owner decision).

// Read-only report: totals from the transcripts, computed locally. It starts no session and needs no trust.
if (args.Contains("--usage-report"))
{
    var projects = args.SkipWhile(a => a != "--projects").Skip(1).FirstOrDefault()
        ?? TranscriptReader.DefaultProjectsRoot;
    var index = TranscriptIndex.Build(projects);
    var t = index.Totals.Total;
    var report = new
    {
        projects,
        files = index.Sessions.Count,
        countedMessages = index.Totals.CountedMessages,
        inputTokens = t.Input,
        outputTokens = t.Output,
        cacheCreationTokens = t.CacheCreation,
        cacheReadTokens = t.CacheRead,
    };
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

// Read-only report: tests the warm and cold rule (D-S7) against the real transcripts. Starts no session.
if (args.Contains("--cache-report"))
{
    var projects = args.SkipWhile(a => a != "--projects").Skip(1).FirstOrDefault()
        ?? TranscriptReader.DefaultProjectsRoot;
    var result = OpenTail.Bullshark.Core.Cache.CacheReport.Compute(projects);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

var options = SupervisorOptions.Parse(args);
if (options.Error is not null)
{
    Console.Error.WriteLine(options.Error);
    Console.Error.WriteLine("usage: OpenTail.Bullshark.Supervisor [--state <state.json>] [--interval 30] [--once] [--workdir <folder>] [--task \"<task>\"]");
    return 2;
}

var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenTail.Bullshark", "logs", "supervisor.log");
Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
void Log(string message)
{
    var line = $"{DateTime.Now:O} {message}";
    Console.WriteLine(line);
    File.AppendAllText(logPath, line + Environment.NewLine);
}

if (!args.Contains("--state")) OpenTail.Bullshark.Core.Storage.AppDataMigration.CopyPreviousIfMissing(Log);
var cli = new ClaudeCli(new ProcessCliRunner());
var store = new StateStore(options.StatePath, Log);
var runStore = new RunStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(options.StatePath))!, "runs.json"));
var runner = new SupervisorRunner(cli, store, TimeProvider.System, log: Log);
var loop = new SupervisorLoop(runner, cli, store, Log);

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };

Log($"supervisor started: state={options.StatePath} interval={options.IntervalSeconds}s once={options.Once}");
var migrated = false;
var announcedNoRuns = false;
do
{
    try
    {
        var agents = await cli.ListAgentsAsync(cancel.Token);

        // The default pair from before runs.json becomes the legacy run, once, and only if it exists (D-S13, D-S17).
        if (!migrated)
        {
            migrated = true;
            var legacy = runStore.MigrateLegacyIfNeeded(store.Load(), agents, options.WorkDir, options.Task, DateTimeOffset.UtcNow);
            if (legacy is not null) Log($"legacy run created for {legacy.WorkingDirectory}");
        }

        var specs = RunStore.SpecsOf(runStore.Load());
        if (specs.Count == 0 && !announcedNoRuns)
        {
            announcedNoRuns = true;
            Log("no run yet: nothing is started until a run is written (start one from the OpenTail.Bullshark window)");
        }

        var (runnable, untrusted) = TrustGate.Filter(specs, TrustCheck.DefaultClaudeJsonPath);
        foreach (var root in untrusted) Log($"waiting for trust: {root} (open it in Claude and accept the trust prompt)");

        var actions = await loop.RunPassAsync(runnable, cancel.Token);
        if (actions > 0) Log($"pass done: {actions} action(s)");
    }
    catch (ClaudeCliException ex)
    {
        Log($"pass failed: {ex.Message}");
    }

    if (options.Once) break;
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds), cancel.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }
} while (!cancel.IsCancellationRequested);

Log("supervisor stopped");
return 0;

/// <summary>Command-line options. Parsing is kept separate so it can be tested.</summary>
sealed record SupervisorOptions(string WorkDir, string Task, int IntervalSeconds, bool Once, string StatePath, string? Error)
{
    public static SupervisorOptions Parse(string[] args)
    {
        string? workdir = null, task = null, state = null;
        int interval = 30;
        bool once = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--workdir": workdir = i + 1 < args.Length ? args[++i] : null; break;
                case "--task": task = i + 1 < args.Length ? args[++i] : null; break;
                case "--state": state = i + 1 < args.Length ? args[++i] : null; break;
                case "--interval":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var seconds) && seconds >= 5) interval = seconds;
                    else return Failed("--interval needs a number of seconds, at least 5");
                    break;
                case "--once": once = true; break;
                default: return Failed($"unknown argument: {args[i]}");
            }
        }

        var statePath = state ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenTail.Bullshark", "state.json");
        var folder = workdir ?? Environment.CurrentDirectory;
        if (!Directory.Exists(folder)) return Failed("--workdir must be an existing folder");
        return new SupervisorOptions(Path.GetFullPath(folder),
            task ?? "Read the project and ask for nothing: wait for the owner's first task.",
            interval, once, Path.GetFullPath(statePath), null);
    }

    private static SupervisorOptions Failed(string error) => new(string.Empty, string.Empty, 0, false, string.Empty, error);
}
