using System.Diagnostics;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Cache;
using OpenTail.Bullshark.Core.Search;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Storage;
using OpenTail.Bullshark.Core.Supervisor;

// Round 10 item 1: times the transcript paths of the app on a projects folder. It only reads. It prints sizes, counts and times, never a
// file, folder or session name, so its output can be written into the repository. A failure prints the exception type only, because a
// message can hold a path.
//
// Usage: OpenTail.Bullshark.Bench [--projects <folder>] [--only index,search,storage,effort,pager,overlap,history,verify,limits] [--repeat <n>] [--cache <file>]
// The default folder is the owner's, read only. Run each step in its own process with --only, so the peak working set belongs to that step.
// --cache gives the index build a cache file of its own (a scratch file, never the owner's): the first build writes it, the next ones read it.

internal static class Program
{
    private const string AllSteps = "index,search,storage,effort,pager,overlap";
    private const double Mb = 1024.0 * 1024.0;

    private static int Main(string[] args)
    {
        try
        {
            Run(args);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"stopped: {ex.GetType().Name} (the message is not printed: it can hold a path)");
            return 1;
        }
    }

    private static void Run(string[] args)
    {
        var projects = Arg(args, "--projects") ?? TranscriptReader.DefaultProjectsRoot;
        var steps = (Arg(args, "--only") ?? AllSteps).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var repeat = int.TryParse(Arg(args, "--repeat"), out var parsedRepeat) && parsedRepeat > 0 ? parsedRepeat : 2;

        var transcripts = Transcripts(projects);
        var folderCount = Directory.Exists(projects) ? Directory.GetDirectories(projects).Length : 0;
        var largest = transcripts.OrderByDescending(f => f.Length).ToList();
        Console.WriteLine($"machine: {Environment.ProcessorCount} logical processors, {Environment.Version}");
        Console.WriteLine($"projects folder: {folderCount} project folders, {transcripts.Count} transcripts, " +
                          $"{transcripts.Sum(f => f.Length) / Mb:0.0} MB, {transcripts.Count(f => f.Length > 20 * Mb)} over 20 MB, " +
                          $"largest {(largest.Count == 0 ? 0 : largest[0].Length / Mb):0.0} MB");
        Console.WriteLine($"steps: {string.Join(", ", steps)}; repeats per step: {repeat}");

        TranscriptIndex? index = null;

        var cachePath = Arg(args, "--cache");
        if (steps.Contains("index"))
        {
            for (var run = 1; run <= repeat; run++)
            {
                var sw = Stopwatch.StartNew();
                index = cachePath is null ? TranscriptIndex.Build(projects) : TranscriptIndex.Build(projects, cachePath);
                sw.Stop();
                Report($"index build{(cachePath is null ? string.Empty : " with cache")}, run {run}", sw.Elapsed,
                    $"sessions {index.Sessions.Count}, messages counted {index.Totals.CountedMessages}, skipped {index.Skipped.Count}");
            }
        }

        if (steps.Contains("history"))
        {
            // The work the window waits for before the history rail is filled: the index, then the end of each transcript, as the window
            // reads it. The window itself is not started here: its startup pass calls the claude CLI, which the round's rules do not allow.
            for (var run = 1; run <= repeat; run++)
            {
                var sw = Stopwatch.StartNew();
                var built = cachePath is null ? TranscriptIndex.Build(projects) : TranscriptIndex.Build(projects, cachePath);
                var tails = 0;
                foreach (var session in built.Sessions)
                {
                    try
                    {
                        if (TranscriptTail.Read(session.FilePath) is not null) tails++;
                    }
                    catch (IOException)
                    {
                        // Left out, as the window leaves it out.
                    }
                }
                sw.Stop();
                Report($"history path, index and tails{(cachePath is null ? string.Empty : " with cache")}, run {run}", sw.Elapsed,
                    $"sessions {built.Sessions.Count}, tails read {tails}");
            }
        }

        if (steps.Contains("limits"))
        {
            // The limit paths of the window: the history (the Limits page and the Away digest) and the status bar's reads (Active, Find). Only counts
            // are printed. Run 1 is the first read of the files in this process; the OS file cache may hold them from earlier runs. The window sizes
            // mirror the code: the history reads from 30 days and five hours back (LimitHistory.DefaultDays, TokenWindow), the status reads from 8 days
            // back (LimitEntries.LimitWindow is internal, so the figure is repeated here), and a tail is the last 256 KB (LimitEntries.Tail, private size).
            var historySince = DateTime.UtcNow - TimeSpan.FromDays(LimitHistory.DefaultDays) - LimitHistory.TokenWindow;
            var statusSince = DateTime.UtcNow - TimeSpan.FromDays(8);
            var historyFiles = transcripts.Where(f => f.LastWriteTimeUtc >= historySince).ToList();
            var statusFiles = transcripts.Where(f => f.LastWriteTimeUtc >= statusSince).ToList();
            Console.WriteLine($"limit windows: history {historyFiles.Count} transcripts, {historyFiles.Sum(f => f.Length) / Mb:0.0} MB; " +
                              $"status {statusFiles.Count} transcripts, {statusFiles.Sum(f => f.Length) / Mb:0.0} MB, " +
                              $"tails {statusFiles.Sum(f => Math.Min(f.Length, 256L * 1024)) / Mb:0.0} MB");
            for (var run = 1; run <= repeat; run++)
            {
                var sw = Stopwatch.StartNew();
                var history = LimitHistory.Read(projects, DateTimeOffset.UtcNow);
                sw.Stop();
                Report($"limit history (LimitHistory.Read), run {run}", sw.Elapsed,
                    $"hits {history.Hits.Count}, days {history.Days.Count}");

                sw.Restart();
                var active = LimitStatus.Active(projects, DateTimeOffset.UtcNow);
                sw.Stop();
                Report($"limit status (LimitStatus.Active), run {run}", sw.Elapsed, $"limits in force {active.Count}");

                sw.Restart();
                var reset = LimitReset.Find(projects, DateTimeOffset.UtcNow);
                sw.Stop();
                Report($"limit reset (LimitReset.Find), run {run}", sw.Elapsed, $"reset in force: {(reset is null ? "no" : "yes")}");
            }
        }

        if (steps.Contains("verify"))
        {
            // The three ways of building the index must give the same index: one transcript after another, in parallel, and with the cache
            // (first written, then read). Only counts and hashes are printed.
            if (cachePath is null)
            {
                Console.WriteLine("verify needs --cache <file>, a scratch file that is written and then read");
                return;
            }
            if (File.Exists(cachePath)) File.Delete(cachePath);
            var reference = TranscriptIndex.Build(projects, TranscriptReader.ReadFile);
            var referenceText = Canonical(reference);
            var parallelText = Canonical(TranscriptIndex.Build(projects));
            var writtenText = Canonical(TranscriptIndex.Build(projects, cachePath));
            var readText = Canonical(TranscriptIndex.Build(projects, cachePath));
            var identical = referenceText == parallelText && referenceText == writtenText && referenceText == readText;
            Console.WriteLine($"verify: sessions {reference.Sessions.Count}, messages counted {reference.Totals.CountedMessages}, " +
                              $"skipped {reference.Skipped.Count}; hashes reader {Hash(referenceText)}, parallel {Hash(parallelText)}, " +
                              $"cache written {Hash(writtenText)}, cache read {Hash(readText)}; identical: {identical}");
        }

        if (steps.Contains("search"))
        {
            index ??= TranscriptIndex.Build(projects);
            var sessions = index.Sessions;
            // A common word, a word nothing contains (every transcript is read), and a Windows path, which the raw-line pre-filter cannot use.
            foreach (var (label, query) in new[] { ("common word", "the"), ("rare word", "zqxjvkwq"), ("Windows path", @"C:\Windows\System32") })
            {
                for (var run = 1; run <= repeat; run++)
                {
                    var sw = Stopwatch.StartNew();
                    var outcome = HistorySearch.RunCounted(sessions, query);
                    sw.Stop();
                    Report($"search, {label}, run {run}", sw.Elapsed, $"sessions matched {outcome.Matches.Count}, unreadable {outcome.Unreadable}");
                }
            }
        }

        if (steps.Contains("storage"))
        {
            for (var run = 1; run <= repeat; run++)
            {
                var sw = Stopwatch.StartNew();
                var folders = StorageReport.Read(projects);
                sw.Stop();
                Report($"storage report, run {run}", sw.Elapsed,
                    $"folders {folders.Count}, transcripts {folders.Sum(p => p.Transcripts)}, {folders.Sum(p => p.Bytes) / Mb:0.0} MB");
            }
        }

        if (steps.Contains("effort"))
        {
            foreach (var (file, position) in largest.Take(3).Select((f, i) => (f, i + 1)))
            {
                for (var run = 1; run <= repeat; run++)
                {
                    var sw = Stopwatch.StartNew();
                    var effort = EffortReport.ReadFile(file.FullName);
                    sw.Stop();
                    Report($"effort report, file {position} ({file.Length / Mb:0.0} MB), run {run}", sw.Elapsed,
                        $"turns {effort.Turns}, failed calls {effort.FailedCalls}");

                    sw.Restart();
                    var turns = TurnFacts.ReadFile(file.FullName);
                    sw.Stop();
                    Report($"turn facts, file {position} ({file.Length / Mb:0.0} MB), run {run}", sw.Elapsed, $"turns {turns.Count}");
                }
            }
        }

        if (steps.Contains("pager") && largest.Count > 0)
        {
            var file = largest[0];
            for (var run = 1; run <= repeat; run++)
            {
                var sw = Stopwatch.StartNew();
                var pager = TranscriptPager.Open(file.FullName);
                sw.Stop();
                Report($"pager open, largest file ({file.Length / Mb:0.0} MB), run {run}", sw.Elapsed, $"lines {pager.LineCount}");

                sw.Restart();
                var first = pager.ReadPage(0, 50);
                sw.Stop();
                Report($"pager first page, run {run}", sw.Elapsed, $"lines shown {first.Count}");

                sw.Restart();
                var last = pager.ReadPage(pager.LastPageStart(50), 50);
                sw.Stop();
                Report($"pager last page, run {run}", sw.Elapsed, $"lines shown {last.Count}");
            }
        }

        if (steps.Contains("overlap"))
        {
            // The five transcripts written most recently stand in for the live sessions of one run.
            var recent = transcripts.OrderByDescending(f => f.LastWriteTimeUtc).Take(5).ToList();
            var recentMb = recent.Sum(f => f.Length) / Mb;
            for (var run = 1; run <= repeat; run++)
            {
                var cache = new SessionWritesCache();
                var sw = Stopwatch.StartNew();
                var writes = recent.Select(f => cache.Read(Path.GetFileNameWithoutExtension(f.Name), f.FullName)).ToList();
                sw.Stop();
                Report($"overlap pass, cold cache, 5 transcripts ({recentMb:0.0} MB), run {run}", sw.Elapsed,
                    $"files written {writes.Sum(w => w.Files.Count)}");

                sw.Restart();
                var again = recent.Select(f => cache.Read(Path.GetFileNameWithoutExtension(f.Name), f.FullName)).ToList();
                sw.Stop();
                Report($"overlap pass, warm cache, 5 transcripts, run {run}", sw.Elapsed, $"files written {again.Sum(w => w.Files.Count)}");

                sw.Restart();
                var specs = writes.Select((_, i) => new SessionSpec(SessionRole.Coder, "s" + (i + 1), Environment.CurrentDirectory,
                    "haiku", "low", "", "acceptEdits")).ToList();
                var runRecord = new RunRecord("bench", "bench", Environment.CurrentDirectory, "acceptEdits", DateTimeOffset.UtcNow, specs,
                    Limits: new RunLimits(1_000_000_000L, null));
                var records = new Dictionary<string, SessionRecord>(StringComparer.Ordinal);
                for (var i = 0; i < specs.Count; i++)
                {
                    records[specs[i].Name] = new SessionRecord(SessionRole.Coder, writes[i].SessionId, specs[i].Name,
                        new DateTimeOffset(recent[i].LastWriteTimeUtc, TimeSpan.Zero), 0, [], null, null);
                }
                var clashes = FileOverlap.Find(runRecord, records, writes);
                var budgets = RunBudget.ForRuns([runRecord], records, writes, DateTimeOffset.UtcNow);
                sw.Stop();
                Report($"clashes and budget, 5 sessions, run {run}", sw.Elapsed, $"clashes {clashes.Count}, budgets {budgets.Count}");
            }
        }
    }

    /// <summary>Every field of an index as text, in order. Only its hash is printed, so no name in it reaches the output.</summary>
    private static string Canonical(TranscriptIndex index)
    {
        var lines = new List<string> { $"total {index.Totals.Total} counted {index.Totals.CountedMessages}" };
        lines.AddRange(index.Totals.ByModel.Select(pair => $"model {pair.Key} {pair.Value}"));
        foreach (var s in index.Sessions)
        {
            lines.Add(string.Join("|",
                s.FilePath, s.SessionId, s.ProjectFolder, s.IsSubagent, s.FirstActivity, s.LastActivity,
                s.Turns, s.UserMessages, s.AssistantMessages, s.ToolCalls,
                string.Join(",", s.ToolNames.Select(pair => $"{pair.Key}={pair.Value}")),
                string.Join(",", s.FilesWritten), s.LastAssistantText, s.LastModel, s.ContextTokensAtLastTurn,
                s.Usage, s.FirstUserText, s.AiTitle, s.CustomTitle, s.LastRecap, s.LastRecapAt, s.Compactions,
                s.ContinuesSessionId, s.FailedToolCalls));
        }
        lines.Add($"skipped {index.Skipped.Count}");
        return string.Join("\n", lines);
    }

    private static string Hash(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..16];

    private static string? Arg(string[] args, string name)
    {
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    private static List<FileInfo> Transcripts(string root) =>
        Directory.Exists(root) ? new DirectoryInfo(root).EnumerateFiles("*.jsonl", SearchOption.AllDirectories).ToList() : [];

    private static void Report(string label, TimeSpan elapsed, string detail)
    {
        var peak = Process.GetCurrentProcess().PeakWorkingSet64 / Mb;
        Console.WriteLine($"{label}: {elapsed.TotalSeconds:0.000} s, peak working set {peak:0} MB, {detail}");
    }
}
