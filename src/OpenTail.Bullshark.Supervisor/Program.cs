using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Cache;

// OpenTail.Bullshark.Supervisor: two read-only reports. It starts, restarts and alerts on nothing.
//
//   OpenTail.Bullshark.Supervisor --usage-report [--projects <folder>]
//   OpenTail.Bullshark.Supervisor --cache-report [--projects <folder>]
//
// The sessions of every run are kept alive by the OpenTail.Bullshark window, the one supervisor (review of 2026-11-15, section 4).
// This process used to run a second supervisor loop beside the window, and two passes could start the same session. A headless
// start at log-on is not wanted now. If it is wanted later, it replaces the window's loop and does not run beside it.
// The sessions talk to each other directly with SendMessage; no process carries messages (owner decision).

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

Console.Error.WriteLine("This process runs no supervisor. The sessions are kept alive by the OpenTail.Bullshark window.");
Console.Error.WriteLine("usage: OpenTail.Bullshark.Supervisor --usage-report | --cache-report [--projects <folder>]");
return 2;
