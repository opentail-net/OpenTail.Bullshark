using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Live;

/// <summary>
/// Live checks of the reviewer setting (D-S36) with the smallest models: Haiku for the planner and the coder, and either a
/// Sonnet subagent or the planner itself as the reviewer. They spend tokens, so they run only when OPENTAIL_BULLSHARK_LIVE=1 and
/// OPENTAIL_BULLSHARK_LIVE_REVIEW=1 are both set. Each works in its own temp folder and state, and stops and removes the sessions it
/// started. Each writes a text report to the temp folder: the subagent calls, the planner's last context size, the token
/// totals, and the states each session passed through.
/// </summary>
[Trait("Category", "Live")]
public sealed class ReviewerLiveTests
{
    private static bool Enabled =>
        Environment.GetEnvironmentVariable("OPENTAIL_BULLSHARK_LIVE") == "1" &&
        Environment.GetEnvironmentVariable("OPENTAIL_BULLSHARK_LIVE_REVIEW") == "1";

    [Fact]
    public async Task ThePlannerReviewsACoderReportThroughASonnetSubagent()
    {
        if (!Enabled) return;

        var run = await RunOnceAsync("sonnet");

        Assert.Equal("HELLO", run.Hello);
        Assert.NotEmpty(run.Subagents);
        Assert.Contains(run.Subagents, m => m.Contains("sonnet", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ThePlannerReviewsItselfWhenSelfIsChosen()
    {
        if (!Enabled) return;

        var run = await RunOnceAsync("self");

        Assert.Equal("HELLO", run.Hello);
        Assert.Empty(run.Subagents);
    }

    /// <summary>
    /// Changes the reviewer during a session. The planner first reviews itself (self). Then a message, sent as a resume prompt to
    /// the idle planner, asks it to review through a Sonnet subagent and gives the coder one more batch. The test checks that the
    /// new batch is written and that the planner then calls a Sonnet subagent.
    /// </summary>
    [Fact]
    public async Task TheReviewerCanBeChangedByMessageMidSession()
    {
        if (!Enabled) return;

        var root = Path.Combine(Path.GetTempPath(), "bullshark-live-switch-" + Guid.NewGuid().ToString("N")[..8]);
        var work = Path.Combine(root, "work");
        Directory.CreateDirectory(work);
        var store = new StateStore(Path.Combine(root, "state", "state.json"));
        var runStore = new RunStore(Path.Combine(root, "state", "runs.json"));
        var cli = new ClaudeCli(new ProcessCliRunner());
        var sessionIds = new List<string>();

        try
        {
            var request = new SetupRequest("plan-code", work,
                "Create a file named hello.txt in this folder that contains exactly the word HELLO. Then report what you did.",
                1, "haiku", "low", "haiku", "low", Reviewer: "self");
            var run = SetupPlan.CreateRun(request, [], DateTimeOffset.UtcNow);
            runStore.Save([run]);

            var runner = new SupervisorRunner(cli, store, TimeProvider.System, resolveTimeout: TimeSpan.FromSeconds(90));
            var loop = new SupervisorLoop(runner, cli, store, _ => { });
            Assert.Equal(2, await loop.RunPassAsync(run.Specs));

            var records = store.Load().Values.ToList();
            sessionIds.AddRange(records.Select(r => r.SessionId));
            var planner = records.Single(r => r.Role == SessionRole.Planner);

            var helloPath = Path.Combine(work, "hello.txt");
            await WaitIdleAsync(cli, sessionIds, () => File.Exists(helloPath), TimeSpan.FromMinutes(12));
            var callsBefore = PlannerSubagentCalls(planner.SessionId).Count;

            // The switch: a message to the idle planner, sent as a resume prompt, with one more batch for the coder.
            var switchPrompt =
                "From now on, review each coder report through a subagent: when a report arrives, call the Agent tool with model sonnet, " +
                "give it the batch and the report, and act on its verdict. Now give the coder one more batch: create a file named bye.txt " +
                "in this folder that contains exactly the word BYE. Send it to the coder with SendMessage. Do not poll for the report: " +
                "end your turn after sending, and the report will arrive as a message.";
            await cli.StartBackgroundAsync(new BackgroundLaunch(
                Name: planner.Name, WorkingDirectory: work, Model: "haiku", Effort: "low",
                Prompt: switchPrompt, PermissionMode: "auto", ResumeSessionId: planner.SessionId));

            var byePath = Path.Combine(work, "bye.txt");
            await WaitIdleAsync(cli, sessionIds, () => File.Exists(byePath), TimeSpan.FromMinutes(8));

            var calls = PlannerSubagentCalls(planner.SessionId);
            var afterSwitch = calls.Skip(callsBefore).ToList();
            var hello = File.Exists(helloPath) ? File.ReadAllText(helloPath).Trim() : "(missing)";
            var bye = File.Exists(byePath) ? File.ReadAllText(byePath).Trim() : "(missing)";
            var report = string.Join(Environment.NewLine, new[]
            {
                $"hello.txt: {hello}",
                $"bye.txt: {bye}",
                $"subagent calls before the switch: {callsBefore}",
                $"subagent calls after the switch: {afterSwitch.Count} ({(afterSwitch.Count == 0 ? "none" : string.Join(", ", afterSwitch))})",
            });
            await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "bullshark-live-switch.txt"), report);

            Assert.Equal("HELLO", hello);
            Assert.Equal("BYE", bye);
            Assert.Equal(0, callsBefore);
            Assert.Contains(afterSwitch, m => m.Contains("sonnet", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await StopAndRemoveAsync(cli, sessionIds);
        }
    }

    /// <summary>Polls until every session is idle and the extra condition holds, or the timeout passes.</summary>
    private static async Task WaitIdleAsync(ClaudeCli cli, IReadOnlyList<string> sessionIds, Func<bool> extra, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            var agents = await cli.ListAgentsAsync();
            var allDone = sessionIds.All(id => agents.FirstOrDefault(a => a.SessionId == id)?.EffectiveState == "done");
            if (allDone && extra()) return;
        }
    }

    /// <summary>Stops and removes only the sessions a test started.</summary>
    private static async Task StopAndRemoveAsync(ClaudeCli cli, IReadOnlyList<string> sessionIds)
    {
        foreach (var sessionId in sessionIds)
        {
            var shortId = ClaudeCli.ShortIdOf(sessionId);
            await cli.StopAsync(shortId);
            var stopBy = DateTimeOffset.UtcNow.AddSeconds(60);
            while (DateTimeOffset.UtcNow < stopBy)
            {
                var agent = (await cli.ListAgentsAsync()).FirstOrDefault(a => a.SessionId == sessionId);
                if (agent is null || agent.EffectiveState != "working") break;
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
            await cli.RemoveAsync(shortId);
        }
    }

    private sealed record RunResult(string Hello, List<string> Subagents, string Report);

    private static async Task<RunResult> RunOnceAsync(string reviewer)
    {
        var root = Path.Combine(Path.GetTempPath(), $"bullshark-live-{reviewer}-" + Guid.NewGuid().ToString("N")[..8]);
        var work = Path.Combine(root, "work");
        Directory.CreateDirectory(work);
        var store = new StateStore(Path.Combine(root, "state", "state.json"));
        var runStore = new RunStore(Path.Combine(root, "state", "runs.json"));
        var cli = new ClaudeCli(new ProcessCliRunner());
        var sessionIds = new List<string>();
        var statesSeen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var request = new SetupRequest("plan-code", work,
                "Create a file named hello.txt in this folder that contains exactly the word HELLO. Then report what you did.",
                1, "haiku", "low", "haiku", "low", Reviewer: reviewer);
            var run = SetupPlan.CreateRun(request, [], DateTimeOffset.UtcNow);
            runStore.Save([run]);

            var runner = new SupervisorRunner(cli, store, TimeProvider.System, resolveTimeout: TimeSpan.FromSeconds(90));
            var loop = new SupervisorLoop(runner, cli, store, _ => { });
            var started = await loop.RunPassAsync(run.Specs);
            Assert.Equal(2, started);

            var records = store.Load().Values.ToList();
            foreach (var record in records)
            {
                sessionIds.Add(record.SessionId);
                names[record.SessionId] = record.Role.ToString().ToLowerInvariant();
                statesSeen[record.SessionId] = new HashSet<string>(StringComparer.Ordinal);
            }
            var planner = records.Single(r => r.Role == SessionRole.Planner);

            // Wait until both sessions are idle again and the file exists, for at most 12 minutes. Record each state seen.
            var deadline = DateTimeOffset.UtcNow.AddMinutes(12);
            var helloPath = Path.Combine(work, "hello.txt");
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                var agents = await cli.ListAgentsAsync();
                foreach (var record in records)
                {
                    var state = agents.FirstOrDefault(a => a.SessionId == record.SessionId)?.EffectiveState ?? "gone";
                    statesSeen[record.SessionId].Add(state);
                }
                var allDone = records.All(r => agents.FirstOrDefault(a => a.SessionId == r.SessionId)?.EffectiveState == "done");
                if (allDone && File.Exists(helloPath)) break;
            }

            var hello = File.Exists(helloPath) ? File.ReadAllText(helloPath).Trim() : "(missing)";
            var subagents = PlannerSubagentCalls(planner.SessionId);
            var plannerContext = PlannerLastContext(planner.SessionId);
            var usage = TokenTotals(sessionIds);
            var states = string.Join("; ", sessionIds.Select(id =>
                $"{names[id]}: {string.Join(",", statesSeen[id].OrderBy(s => s, StringComparer.Ordinal))}"));

            var report = string.Join(Environment.NewLine, new[]
            {
                $"reviewer: {reviewer}",
                $"hello.txt: {hello}",
                $"planner subagent calls: {subagents.Count}",
                $"subagent models: {(subagents.Count == 0 ? "none" : string.Join(", ", subagents))}",
                $"planner last context (tokens): {plannerContext:N0}",
                $"token totals (input, output, cache read, cache write): {usage}",
                $"states seen: {states}",
            });
            await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), $"bullshark-live-review-{reviewer}.txt"), report);

            return new RunResult(hello, subagents, report);
        }
        finally
        {
            // Stop and remove only the sessions this test started. The owner's sessions are never listed here.
            foreach (var sessionId in sessionIds)
            {
                var shortId = ClaudeCli.ShortIdOf(sessionId);
                await cli.StopAsync(shortId);
                var stopBy = DateTimeOffset.UtcNow.AddSeconds(60);
                while (DateTimeOffset.UtcNow < stopBy)
                {
                    var agent = (await cli.ListAgentsAsync()).FirstOrDefault(a => a.SessionId == sessionId);
                    if (agent is null || agent.EffectiveState != "working") break;
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
                await cli.RemoveAsync(shortId);
            }
        }
    }

    /// <summary>The models the planner's subagent calls asked for, read from its transcript (and its subagents' folder).</summary>
    private static List<string> PlannerSubagentCalls(string plannerSessionId)
    {
        var models = new List<string>();
        foreach (var file in Directory.EnumerateFiles(TranscriptReader.DefaultProjectsRoot, "*.jsonl", SearchOption.AllDirectories)
                     .Where(f => f.Contains(plannerSessionId, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (!line.Contains("\"tool_use\"", StringComparison.Ordinal)) continue;
                using var document = JsonDocument.Parse(line);
                if (!document.RootElement.TryGetProperty("message", out var message) ||
                    !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var type) && type.GetString() == "tool_use" &&
                        block.TryGetProperty("name", out var name) && name.GetString() is "Agent" or "Task")
                    {
                        var model = block.TryGetProperty("input", out var input) && input.TryGetProperty("model", out var m)
                            ? m.GetString() ?? "(none)"
                            : "(none)";
                        models.Add(model);
                    }
                }
            }
        }
        return models;
    }

    /// <summary>The context the planner's own session held at its last request (not its subagents').</summary>
    private static long PlannerLastContext(string plannerSessionId)
    {
        long last = 0;
        foreach (var file in Directory.EnumerateFiles(TranscriptReader.DefaultProjectsRoot, "*.jsonl", SearchOption.AllDirectories)
                     .Where(f => f.Contains(plannerSessionId, StringComparison.OrdinalIgnoreCase) &&
                                 !f.Contains("subagents", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (!line.Contains("\"usage\"", StringComparison.Ordinal)) continue;
                using var document = JsonDocument.Parse(line);
                if (!document.RootElement.TryGetProperty("message", out var message) ||
                    !message.TryGetProperty("usage", out var usage)) continue;
                last = Num(usage, "input_tokens") + Num(usage, "cache_read_input_tokens") + Num(usage, "cache_creation_input_tokens");
            }
        }
        return last;
    }

    /// <summary>Token totals over the transcripts of the sessions this test started, and their subagents.</summary>
    private static string TokenTotals(IReadOnlyList<string> sessionIds)
    {
        long input = 0, output = 0, read = 0, write = 0;
        foreach (var file in Directory.EnumerateFiles(TranscriptReader.DefaultProjectsRoot, "*.jsonl", SearchOption.AllDirectories)
                     .Where(f => sessionIds.Any(id => f.Contains(id, StringComparison.OrdinalIgnoreCase))))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (!line.Contains("\"usage\"", StringComparison.Ordinal)) continue;
                using var document = JsonDocument.Parse(line);
                if (!document.RootElement.TryGetProperty("message", out var message) ||
                    !message.TryGetProperty("usage", out var usage)) continue;
                input += Num(usage, "input_tokens");
                output += Num(usage, "output_tokens");
                read += Num(usage, "cache_read_input_tokens");
                write += Num(usage, "cache_creation_input_tokens");
            }
        }
        return $"{input:N0}, {output:N0}, {read:N0}, {write:N0}";
    }

    private static long Num(JsonElement element, string property) =>
        element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
}
