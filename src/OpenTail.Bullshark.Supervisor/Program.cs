using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Cache;
using OpenTail.Bullshark.Core.Reports;

// OpenTail.Bullshark.Supervisor: two read-only reports. It starts, restarts and alerts on nothing.
//
//   OpenTail.Bullshark.Supervisor --usage-report [--projects <folder>]
//   OpenTail.Bullshark.Supervisor --cache-report [--projects <folder>]
//
// The sessions of every run are kept alive by the OpenTail.Bullshark window, the one supervisor (review of 2026-11-15, section 4).
// This process used to run a second supervisor loop beside the window, and two passes could start the same session. A headless
// start at log-on is not wanted now. If it is wanted later, it replaces the window's loop and does not run beside it.
// The sessions talk to each other directly with SendMessage; no process carries messages (owner decision).

try
{
    return Run(args);
}
catch (Exception ex)
{
    // A failure (a folder that cannot be read, for one) is printed here with exit code 1, not as an unhandled exception trace.
    Console.Error.WriteLine($"OpenTail.Bullshark.Supervisor failed: {ex.Message}");
    return 1;
}

static int Run(string[] args)
{
    const string usage = "usage: OpenTail.Bullshark.Supervisor --usage-report | --cache-report [--projects <folder>]";

    // A wrong command line (a --projects with no folder after it, for one) is reported and exits 2. It is never read as a folder.
    var command = HostCommand.Parse(args, TranscriptReader.DefaultProjectsRoot);
    if (command.Problem is not null)
    {
        Console.Error.WriteLine(command.Problem);
        Console.Error.WriteLine(usage);
        return 2;
    }

    // Read-only report: totals from the transcripts, computed locally. It starts no session and needs no trust.
    if (command.Report == HostReport.Usage)
    {
        var projects = command.Projects;
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
    if (command.Report == HostReport.Cache)
    {
        var result = CacheReport.Compute(command.Projects);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    Console.Error.WriteLine("This process runs no supervisor. The sessions are kept alive by the OpenTail.Bullshark window.");
    Console.Error.WriteLine(usage);
    return 2;
}
