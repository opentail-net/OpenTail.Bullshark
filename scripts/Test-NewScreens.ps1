# New screens, first look (round 4, item 5). Repeats the screenshots of the screens built on 2026-11-17: the Needs you group, the
# setup page (start time and limits), Storage, Timeline, a transcript with the effort panel and Export, and Changes.
#
# Everything the window reads is a scratch fixture, built by this script in -Out: a state folder with one run of two coders (their
# sessions do not exist), a test .claude folder with one transcript, a git repo with one edited file, and a stand-in for the claude CLI
# that lists no sessions and starts nothing. The stand-in answers `daemon status` and `--version` from FAKECLAUDE_* variables (the daemon
# screens set them) and logs every call to calls.log under -Out\tools, so a run can be checked for read-only calls only. Each screen is one run of the built App with --no-pass, --state and --claude-home set to
# that scratch, so no Claude session is started and the owner's transcripts and state are not read as screens. The app's own
# claude listing and attach calls reach only the stand-in, because its folder is first on PATH for these runs.
#
#   pwsh scripts\Test-NewScreens.ps1 [-Exe <path to OpenTail.Bullshark.App.exe>] [-Out <scratch folder>]
#
# Each run prints PASS or FAIL: the app exits 0 (it closes itself after the screenshot) and the PNG is written. The script exits 1 if
# any check fails. The PNGs are for a person to look at; they are not committed.
param(
    [string]$Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\OpenTail.Bullshark.App\bin\Debug\net10.0-windows10.0.19041.0\OpenTail.Bullshark.App.exe'),
    [string]$Out = (Join-Path $env:TEMP 'bullshark-newscreens')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Exe)) { throw "The App is not built: $Exe (run dotnet build OpenTail.Bullshark.slnx first)" }
$Exe = (Resolve-Path -LiteralPath $Exe).Path
$core = (Resolve-Path -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\OpenTail.Bullshark.Core\OpenTail.Bullshark.Core.csproj')).Path

$state = Join-Path $Out 'state'
$claudeHome = Join-Path $Out 'home\.claude'
$repo = Join-Path $Out 'repo'
$tools = Join-Path $Out 'tools'
$shots = Join-Path $Out 'shots'
# Only the folders this script makes are cleared; -Out itself is kept.
foreach ($folder in @($state, $claudeHome, $repo, $tools, $shots)) { if (Test-Path $folder) { Remove-Item -Recurse -Force -LiteralPath $folder } }
New-Item -ItemType Directory -Force -Path $state, $repo, $shots, "$tools\gen", "$tools\fakeclaude" | Out-Null

# The fixture generator (the Core library writes the state files, so their format is the app's own) and the stand-in for claude.
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$core" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath "$tools\gen\gen.csproj" -Encoding UTF8

@'
using System.Text.Json;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Supervisor;

// Scratch fixtures for the screenshots. Usage: gen <stateFolder> <repoFolder> <claudeHomeFolder>. Session ids are made up.
var stateFolder = args[0];
var repo = args[1];
var claudeHome = args[2];
Directory.CreateDirectory(stateFolder);
var now = DateTimeOffset.UtcNow;

const string runId = "r4a1";
var pattern = PatternSpec.Find("plan-many")!;
var specs = RunPlan.Build(pattern, repo, "Scratch task for the screenshots: nothing runs.", runId, 2);
var run = new RunRecord(runId, "plan-many", repo, "auto", now.AddMinutes(-95), specs);
// A second run that waits for its start time, seven hours from now (night batch N6): the Night page's Runs row and the Sleep row count it.
// It is created before the first run, so the window still opens on the first one.
const string waitingRunId = "r4w2";
var waitingSpecs = RunPlan.Build(pattern, repo, "Scratch waiting run: nothing runs until its start time.", waitingRunId, 2);
var waitingRun = new RunRecord(waitingRunId, "plan-many", repo, "auto", now.AddHours(-3), waitingSpecs, StartAt: now.AddHours(7));
new RunStore(Path.Combine(stateFolder, "runs.json")).Save([run, waitingRun]);

var planner = specs.First(s => s.Role == SessionRole.Planner);
var coders = specs.Where(s => s.Role == SessionRole.Coder).ToList();
const string plannerId = "5e4f3a21-0000-4000-8000-00000000a0a0";
const string coder1Id = "5e4f3a21-0000-4000-8000-00000000c0d1";
const string coder2Id = "5e4f3a21-0000-4000-8000-00000000c0d2";

var records = new Dictionary<string, SessionRecord>(StringComparer.Ordinal)
{
    [planner.Name] = new(SessionRole.Planner, plannerId, planner.Name, now.AddMinutes(-95), 0, [], "working", null),
    [coders[0].Name] = new(SessionRole.Coder, coder1Id, coders[0].Name, now.AddMinutes(-90), 3,
        [now.AddMinutes(-30), now.AddMinutes(-20), now.AddMinutes(-8)], "working",
        "RestartLoop: restarted 3 times in 25 minutes"),
    [coders[1].Name] = new(SessionRole.Coder, coder2Id, coders[1].Name, now.AddMinutes(-90), 0, [], "stopped",
        "Stopped: stopped by the owner"),
};
new StateStore(Path.Combine(stateFolder, "state.json")).Save(records);

var events = new EventLog(Path.Combine(stateFolder, "events.jsonl"));
void Event(int minutesAgo, string session, string kind, string detail) =>
    events.Write(new SupervisorEvent(now.AddMinutes(-minutesAgo), session, kind, detail));
Event(95, planner.Name, "start", "started with its brief");
Event(94, coders[0].Name, "start", "started with its brief");
Event(93, coders[1].Name, "start", "started with its brief");
Event(60, coders[0].Name, "state", "working -> blocked");
Event(59, coders[0].Name, "adopt", "adopted after the window restarted");
Event(30, coders[0].Name, "resume", "resumed as a new session");
Event(30, coders[0].Name, "alert:RestartLoop", "restarted 3 times in 25 minutes");
Event(20, coders[1].Name, "state", "working -> stopped");
Event(8, coders[0].Name, "alert:RestartLoop", "restarted 3 times in 25 minutes");

// The coder's transcript, in the shapes the effort and export tests write.
// A working folder outside the Windows temp folder, which the History section hides under its temporary rule (owner, 2026-11-15). Only a label.
var fixtureFolder = @"C:\Git\scratch-repo";
var lines = new List<object>();
var at = now.AddMinutes(-88);
string Stamp(int seconds) => at.AddSeconds(seconds).ToString("O");
object Usage(long input, long output) => new { input_tokens = input, output_tokens = output, cache_creation_input_tokens = 500L, cache_read_input_tokens = 2000L };
lines.Add(new { type = "permission-mode", permissionMode = "auto", sessionId = coder1Id });
// The first prompt names the working folder (cwd), as Claude Code writes it: the rail and the Storage page show the folder's leaf (P9), not the folder key.
lines.Add(new { type = "user", timestamp = Stamp(0), cwd = fixtureFolder, message = new { role = "user", content = "Write the notes for the batch in notes.md, then run the build." } });
var callNo = 0;
object Call(string name, object input)
{
    callNo++;
    return new
    {
        type = "assistant",
        timestamp = Stamp(5 + callNo * 4),
        requestId = "r-" + callNo,
        message = new
        {
            id = "m-" + callNo,
            model = "claude-opus-5-5",
            content = new object[] { new { type = "tool_use", id = "toolu_" + callNo, name, input } },
            usage = Usage(10, 5),
        },
    };
}
for (var i = 0; i < 5; i++) lines.Add(Call("Read", new { file_path = Path.Combine(repo, "README.md") }));
lines.Add(new { type = "user", timestamp = Stamp(40), message = new { role = "user", content = new object[] { new { type = "tool_result", content = "README read", tool_use_id = "toolu_1" } } } });
for (var i = 0; i < 3; i++) lines.Add(Call("Bash", new { command = "dotnet build" }));
lines.Add(Call("Bash", new { command = "dotnet test --no-build" }));
lines.Add(new { type = "user", timestamp = Stamp(70), message = new { role = "user", content = new object[] { new { type = "tool_result", content = "error: the build failed", is_error = true, tool_use_id = "toolu_7" } } } });
// The same lint call failing three times in a row, with no success in between: the failing loop of the effort panel (features batch G3).
for (var i = 0; i < 3; i++)
{
    lines.Add(Call("Bash", new { command = "npm run lint" }));
    lines.Add(new { type = "user", timestamp = Stamp(100 + i), message = new { role = "user", content = new object[] { new { type = "tool_result", content = "lint: 4 errors", is_error = true, tool_use_id = "toolu_" + callNo } } } });
}
lines.Add(Call("Edit", new { file_path = Path.Combine(repo, "notes.md"), old_string = "draft", new_string = "batch 1 notes" }));
lines.Add(new { type = "assistant", timestamp = Stamp(90), requestId = "r-text", message = new { id = "m-text", model = "claude-opus-5-5", content = new object[] { new { type = "text", text = "Batch 1 notes are written. The build failed once and is fixed." } }, usage = Usage(3, 40) } });
// The last request carries 170,003 tokens (ctx 170k): at the assumed 200k window that is 85 %, so the warning mark shows (ctx meter, Part 1).
lines.Add(new { type = "assistant", timestamp = Stamp(91), requestId = "r-big", message = new { id = "m-big", model = "claude-opus-5-5", content = new object[] { new { type = "text", text = "The context is large now." } }, usage = new { input_tokens = 3L, output_tokens = 40L, cache_creation_input_tokens = 0L, cache_read_input_tokens = 170000L } } });
lines.Add(new { type = "system", subtype = "turn_duration", durationMs = 92000, timestamp = Stamp(92) });
var projectFolder = Path.Combine(claudeHome, "projects", OpenTail.Bullshark.Core.Folders.FolderKey.From(fixtureFolder));
Directory.CreateDirectory(projectFolder);
File.WriteAllText(Path.Combine(projectFolder, coder1Id + ".jsonl"), string.Join("\n", lines.Select(line => JsonSerializer.Serialize(line))) + "\n");

// Two more past sessions, for Group by tag (features batch G4): one tagged "docs" and one with no tag at all.
const string docsId = "5e4f3a21-0000-4000-8000-00000000d0d1";
const string untaggedId = "5e4f3a21-0000-4000-8000-00000000d0d2";
void SmallSession(string id, string prompt, int minutesAgo)
{
    var when = now.AddMinutes(-minutesAgo).ToString("O");
    var rows = new[]
    {
        JsonSerializer.Serialize(new { type = "user", timestamp = when, message = new { role = "user", content = prompt } }),
        JsonSerializer.Serialize(new { type = "assistant", timestamp = when, requestId = "r-" + id, message = new { id = "m-" + id, model = "claude-opus-5-5", content = new object[] { new { type = "text", text = "Done." } }, usage = new { input_tokens = 3L, output_tokens = 5L, cache_creation_input_tokens = 0L, cache_read_input_tokens = 100L } } }),
        JsonSerializer.Serialize(new { type = "system", subtype = "turn_duration", durationMs = 1000, timestamp = when }),
    };
    File.WriteAllText(Path.Combine(projectFolder, id + ".jsonl"), string.Join("\n", rows) + "\n");
}
SmallSession(docsId, "Tidy the docs index.", 200);
SmallSession(untaggedId, "Check the file names.", 210);

// One tag on the coder's session, for the tag-filter screen (--rail-tag screenshot): only its rows stay in the rail under that filter.
// A plan file on the same session (plan progress, Part 2): 2 of 5 items are done, so its row shows 2/5 and its next item is "Run the build".
var planPath = Path.GetFullPath(Path.Combine(stateFolder, "plan.md"));
File.WriteAllText(planPath, "# Batch plan (screenshot fixture)\n\n- [x] Read the brief\n- [x] Write notes.md\n- [ ] Run the build\n  - [ ] Nested: run the tests\n- [!] Ask the owner about the rename\n");
// The coder's session has two tags, so Group by tag lists it under both (features batch G4); the docs session has one.
new SessionLibrary(Path.Combine(stateFolder, "library.json")).Update(entries =>
{
    entries[coder1Id] = new LibraryEntry(Tags: ["screenshot", "batch"], PlanFile: planPath);
    entries[docsId] = new LibraryEntry(Tags: ["docs"]);
});

Console.WriteLine($"coder1Id={coder1Id}");
Console.WriteLine($"coder1={coders[0].Name}");
'@ | Set-Content -LiteralPath "$tools\gen\Program.cs" -Encoding UTF8

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>claude</AssemblyName>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
  </PropertyGroup>
</Project>
"@ | Set-Content -LiteralPath "$tools\fakeclaude\fakeclaude.csproj" -Encoding UTF8

@'
// A stand-in for the claude CLI during the screenshots. It lists no sessions, attaches to nothing and starts nothing.
// It answers `daemon status` and `--version` (round 7 item 1 and 2) from three variables, and appends every call to FAKECLAUDE_LOG:
//   FAKECLAUDE_DAEMON=running|down      (default running: the daemon is up; down prints the text `claude daemon status` printed on
//                                        2.1.326 when it was not running, with exit code 1)
//   FAKECLAUDE_VERSION=2.1.326          (what `claude --version` prints)
//   FAKECLAUDE_DAEMON_VERSION=          (the version the running daemon reports; default the same as FAKECLAUDE_VERSION)
var callLog = Environment.GetEnvironmentVariable("FAKECLAUDE_LOG");
if (!string.IsNullOrEmpty(callLog)) File.AppendAllText(callLog, string.Join(' ', args) + "\n");
var cliVersion = Environment.GetEnvironmentVariable("FAKECLAUDE_VERSION") ?? "2.1.326";
var daemonVersion = Environment.GetEnvironmentVariable("FAKECLAUDE_DAEMON_VERSION") ?? cliVersion;
var daemonDown = Environment.GetEnvironmentVariable("FAKECLAUDE_DAEMON") == "down";
const string NotRunning = @"not running

bg sessions:
  sock dir:     \\.\pipe\cc-daemon-*
  control.sock: unreachable (connect ENOENT \\.\pipe\cc-daemon-*-control)
  bg workers:   0 in roster.json (control unreachable)
  roster.json:  updated 6604s ago
  daemon.log:   31.7KB at C:\Users\example\.claude\daemon.log
";
// `agents --json` prints FAKECLAUDE_AGENTS when it is set (a JSON array), else no sessions (round 7 item 1, revised: a listing with sessions
// that claim working or blocked while the daemon is down).
if (args.Length > 0 && args[0] == "agents") Console.Out.Write(Environment.GetEnvironmentVariable("FAKECLAUDE_AGENTS") ?? "[]");
else if (args.Length >= 2 && args[0] == "daemon" && args[1] == "status")
{
    if (daemonDown) { Console.Out.Write(NotRunning); return 1; }
    Console.Out.Write("running (pid 4312, version " + daemonVersion + ", uptime 2h 5m)\n\nbg sessions:\n" +
        "  sock dir:     \\\\.\\pipe\\cc-daemon-*\n" +
        "  control.sock: listening (\\\\.\\pipe\\cc-daemon-*-control)\n" +
        "  bg workers:   0 in roster.json\n" +
        "  roster.json:  updated 3s ago\n" +
        "  daemon.log:   12.4KB at C:\\Users\\example\\.claude\\daemon.log\n");
}
else if (args.Length > 0 && args[0] == "--version") Console.Out.Write(cliVersion + " (Claude Code)\n");
return 0;
'@ | Set-Content -LiteralPath "$tools\fakeclaude\Program.cs" -Encoding UTF8

& dotnet build "$tools\gen\gen.csproj" -o "$tools\gen\out" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "The fixture generator did not build (see dotnet build output)" }
& dotnet build "$tools\fakeclaude\fakeclaude.csproj" -o "$tools\fakebin" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "The stand-in for claude did not build" }

# The git repo: one committed file and one edited file, so the Changes tab has a change to show. Two more files make the big-deletion
# check (features batch G2) show: old-notes.md loses 55 of its 60 lines (mostly deleted), and retired.md is deleted.
$git = @('-c', 'user.name=scratch', '-c', 'user.email=scratch@example.invalid')
& git init -q $repo
Set-Content -LiteralPath "$repo\README.md" -Value "# Scratch repo`nNothing here is real.`n" -Encoding UTF8
Set-Content -LiteralPath "$repo\notes.md" -Value "draft`n" -Encoding UTF8
Set-Content -LiteralPath "$repo\old-notes.md" -Value ((1..60 | ForEach-Object { "old line $_" }) -join "`n") -Encoding UTF8
Set-Content -LiteralPath "$repo\retired.md" -Value "retired`n" -Encoding UTF8
& git -C $repo add README.md notes.md old-notes.md retired.md
& git -C $repo @git commit -q -m "Scratch base"
Set-Content -LiteralPath "$repo\README.md" -Value "# Scratch repo`nNothing here is real. Edited for the screenshot.`n" -Encoding UTF8
Set-Content -LiteralPath "$repo\old-notes.md" -Value ((1..5 | ForEach-Object { "kept line $_" }) -join "`n") -Encoding UTF8
Remove-Item -LiteralPath "$repo\retired.md"
# notes.md is edited and staged (git add), and README.md stays unstaged: the Changes tab must show the staged hunks next to the unstaged
# ones (review F2, git diff HEAD).
Set-Content -LiteralPath "$repo\notes.md" -Value "draft`nstaged for the screenshot`n" -Encoding UTF8
& git -C $repo add notes.md

# The viewed ticks of the coder's Changes tab (features batch G1), written into ui.json for the two Changes screens only: README.md is ticked
# with its current stamp (viewed); retired.md is ticked as deleted (viewed); old-notes.md has a stale stamp, so its tick does not count (it
# changed after the tick). The stamp is "length:last-write ticks", as the app reads it. Changed: 4, 2 of 4 viewed (notes.md is staged and not ticked).
$coderSession = '5e4f3a21-0000-4000-8000-00000000c0d1'
$readme = Get-Item -LiteralPath "$repo\README.md"
$ticks = [ordered]@{
    $coderSession = [ordered]@{
        at = (Get-Date).ToUniversalTime().ToString('o')
        files = [ordered]@{ 'README.md' = "$($readme.Length):$($readme.LastWriteTimeUtc.Ticks)"; 'retired.md' = 'deleted'; 'old-notes.md' = '1:1' }
    }
}
$viewedExtra = ', "viewedFiles": ' + ($ticks | ConvertTo-Json -Depth 6 -Compress)

& dotnet "$tools\gen\out\gen.dll" $state $repo $claudeHome
if ($LASTEXITCODE -ne 0) { throw "The fixture generator failed" }

# The listing for the daemon-down screens: the planner is listed as blocked and one coder as working, while the daemon is not running. Both
# claim working or blocked, so the Needs-you note says two sessions are not alive (round 7 item 1, revised). Ids are the fixture's.
$daemonDownListing = '[{"id":"a0a0a0a0","kind":"background","sessionId":"5e4f3a21-0000-4000-8000-00000000a0a0","name":"bullshark-planner-r4a1","state":"blocked"},' +
    '{"id":"c0d1c0d1","kind":"background","sessionId":"5e4f3a21-0000-4000-8000-00000000c0d1","name":"bullshark-coder-r4a1-1","state":"working"}]'

# Each screen: a theme for the rail (ui.json beside the state file), the options that open the screen, and a name for its PNG.
$screens = @(
    @{ Name = 'rail-dark'; Theme = 'Dark'; Options = @() },
    @{ Name = 'rail-light'; Theme = 'Light'; Options = @() },
    # Tags and automatic naming, Part A: the rail with the tag filter chosen (--rail-tag, test only). Only the rows of the tagged session stay.
    @{ Name = 'rail-tag-dark'; Theme = 'Dark'; Options = @('--rail-tag', 'screenshot') },
    # Group by tag (features batch G4): the History section lists tag groups; the coder's session is in both of its tag groups.
    @{ Name = 'rail-group-tag-dark'; Theme = 'Dark'; Options = @('--rail-group', 'tag') },
    @{ Name = 'setup-dark'; Theme = 'Dark'; Options = @('--open-tab', 'setup', '--setup-detail', 'models') },
    @{ Name = 'setup-light'; Theme = 'Light'; Options = @('--open-tab', 'setup', '--setup-detail', 'models') },
    @{ Name = 'storage-dark'; Theme = 'Dark'; Options = @('--open-tab', 'storage') },
    @{ Name = 'timeline-dark'; Theme = 'Dark'; Options = @('--open-tab', 'timeline') },
    @{ Name = 'timeline-light'; Theme = 'Light'; Options = @('--open-tab', 'timeline') },
    # Insights and Settings (2026-11-18): the four sub-tabs of Insights, the three of Settings. The Storage light screen checks the theme of the
    # Storage page (it had none). Each opens its own sub-tab; the status bar is in every screen.
    @{ Name = 'insights-away-dark'; Theme = 'Dark'; Options = @('--open-tab', 'away') },
    @{ Name = 'insights-usage-dark'; Theme = 'Dark'; Options = @('--open-tab', 'usage') },
    @{ Name = 'insights-timeline-dark'; Theme = 'Dark'; Options = @('--open-tab', 'timeline') },
    @{ Name = 'insights-storage-dark'; Theme = 'Dark'; Options = @('--open-tab', 'storage') },
    @{ Name = 'insights-storage-light'; Theme = 'Light'; Options = @('--open-tab', 'storage') },
    @{ Name = 'settings-general-dark'; Theme = 'Dark'; Options = @('--open-tab', 'settings') },
    # The Night page (night batch N6): the bedtime preflight. The light one has quiet hours on, so its quiet line says so.
    @{ Name = 'insights-night-dark'; Theme = 'Dark'; Options = @('--open-tab', 'night') },
    @{ Name = 'insights-night-light'; Theme = 'Light'; Options = @('--open-tab', 'night'); UiExtra = ', "quietHours": { "from": "22:00", "to": "07:00" }' },
    # Quiet hours in Settings, General (night batch N4): a preset on the dark one, and a window set by hand, shown as Custom, on the light one.
    @{ Name = 'settings-quiet-dark'; Theme = 'Dark'; Options = @('--open-tab', 'settings'); UiExtra = ', "quietHours": { "from": "22:00", "to": "07:00" }' },
    @{ Name = 'settings-quiet-custom-light'; Theme = 'Light'; Options = @('--open-tab', 'settings'); UiExtra = ', "quietHours": { "from": "21:30", "to": "06:30" }' },
    @{ Name = 'settings-general-light'; Theme = 'Light'; Options = @('--open-tab', 'settings') },
    @{ Name = 'settings-naming-dark'; Theme = 'Dark'; Options = @('--open-tab', 'settings-naming') },
    @{ Name = 'settings-claude-dark'; Theme = 'Dark'; Options = @('--open-tab', 'settings-claude') },
    @{ Name = 'transcript-dark'; Theme = 'Dark'; Options = @('--open-transcript', '5e4f3a21-0000-4000-8000-00000000c0d1') },
    # The effort panel open (--effort-open), in both themes: its contents are what the screens check (round 5 item 16).
    @{ Name = 'effort-dark'; Theme = 'Dark'; Options = @('--open-transcript', '5e4f3a21-0000-4000-8000-00000000c0d1', '--effort-open') },
    @{ Name = 'effort-light'; Theme = 'Light'; Options = @('--open-transcript', '5e4f3a21-0000-4000-8000-00000000c0d1', '--effort-open') },
    # The Changes tab with the flags of the big-deletion check and the viewed ticks (features batch G1 and G2), in both themes.
    @{ Name = 'changes-dark'; Theme = 'Dark'; Options = @('--open-tab', 'changes:bullshark-coder-r4a1-1'); UiExtra = $viewedExtra },
    @{ Name = 'changes-light'; Theme = 'Light'; Options = @('--open-tab', 'changes:bullshark-coder-r4a1-1'); UiExtra = $viewedExtra },
    # Round 7 item 1: the daemon is not running, so the Needs you group says so and the pass starts nothing.
    # The listing has two sessions that claim working or blocked, while the daemon is not running: the Needs-you note counts them, and the
    # rail and the tab show them as "not running (daemon idle)".
    @{ Name = 'daemon-down-dark'; Theme = 'Dark'; Options = @(); Env = @{ FAKECLAUDE_DAEMON = 'down'; FAKECLAUDE_AGENTS = $daemonDownListing } },
    @{ Name = 'daemon-down-light'; Theme = 'Light'; Options = @(); Env = @{ FAKECLAUDE_DAEMON = 'down'; FAKECLAUDE_AGENTS = $daemonDownListing } },
    # Round 7 item 2: the running daemon reports 2.1.300 and claude is 2.1.326, so the Respawn all button shows in the bar.
    @{ Name = 'daemon-drift-dark'; Theme = 'Dark'; Options = @(); Env = @{ FAKECLAUDE_DAEMON = 'running'; FAKECLAUDE_DAEMON_VERSION = '2.1.300' } },
    # Round 7 item 5: the Storage page says how long Claude keeps transcripts. This screen has a settings.json that sets 45 days; the
    # default screens above have none (Settings is written for this screen only, and removed after it).
    @{ Name = 'storage-retention-dark'; Theme = 'Dark'; Options = @('--open-tab', 'storage'); Settings = '{ "cleanupPeriodDays": 45 }' },
    # Toasts by event and the ctx meter (2026-11-18). UiExtra adds keys to ui.json. The toast switch off dims the five kinds of toast (Settings, General).
    @{ Name = 'settings-toasts-off-dark'; Theme = 'Dark'; Options = @('--open-tab', 'settings'); UiExtra = ', "toast": false' },
    # A context window that is not a preset is shown as Custom (N), and two kinds of toast are switched off.
    @{ Name = 'settings-window-custom-light'; Theme = 'Light'; Options = @('--open-tab', 'settings');
       UiExtra = ', "contextWindow": 300000, "toastEvents": { "notices": false, "ready": false }' },
    # The same rail as rail-dark with a 1M window: the 170k row has no warning mark, and its figure is the same.
    @{ Name = 'rail-ctx-1m-dark'; Theme = 'Dark'; Options = @(); UiExtra = ', "contextWindow": 1000000' }
)

# The stand-in goes first on PATH for these runs only: the window's claude calls reach it, not the owner's claude.
$env:PATH = "$tools\fakebin;" + $env:PATH
$env:FAKECLAUDE_LOG = "$tools\calls.log"
$failed = 0
foreach ($screen in $screens) {
    $name = $screen.Name
    # Each screen sets its own stand-in answers and clears the others, so one screen's daemon does not leak into the next.
    foreach ($key in 'FAKECLAUDE_DAEMON', 'FAKECLAUDE_DAEMON_VERSION', 'FAKECLAUDE_AGENTS') { Remove-Item "Env:$key" -ErrorAction SilentlyContinue }
    if ($screen.Env) { foreach ($key in $screen.Env.Keys) { Set-Item -Path "Env:$key" -Value $screen.Env[$key] } }
    # The settings file of the test home is there only for the screens that set it (round 7 item 5).
    $settingsPath = Join-Path $claudeHome 'settings.json'
    if ($screen.Settings) { Set-Content -LiteralPath $settingsPath -Value $screen.Settings -Encoding UTF8 } else { Remove-Item -LiteralPath $settingsPath -ErrorAction SilentlyContinue }
    # ui.json: the theme, and any extra keys the screen names (UiExtra, a JSON fragment that starts with a comma).
    Set-Content -LiteralPath "$state\ui.json" -Value ('{"railTheme": "' + $screen.Theme + '"' + $screen.UiExtra + '}') -Encoding UTF8
    # The heartbeat of the last supervisor pass (night batch N1), written as just now, so the Night page shows a fresh pass on every screen.
    Set-Content -LiteralPath "$state\heartbeat.json" -Value ('{"at": "' + (Get-Date).ToUniversalTime().ToString('o') + '", "pass": 7}') -Encoding UTF8
    $png = Join-Path $shots "$name.png"
    $argList = @('--state', "$state\state.json", '--no-pass', '--workdir', $repo, '--claude-home', $claudeHome,
        '--shot', $png, '--shot-after', '9') + $screen.Options
    $process = Start-Process -FilePath $Exe -ArgumentList $argList -PassThru -WorkingDirectory $repo
    if (-not $process.WaitForExit(60000)) {
        Stop-Process -Id $process.Id -Force
        Write-Output "FAIL ${name}: the window did not close within 60 s (it was stopped)"
        $failed++
        continue
    }
    $pictureOk = (Test-Path -LiteralPath $png) -and ((Get-Item -LiteralPath $png).Length -gt 0)
    if ($process.ExitCode -eq 0 -and $pictureOk) {
        Write-Output "PASS ${name}: exit 0, $png"
    } else {
        Write-Output "FAIL ${name}: exit $($process.ExitCode), picture written: $pictureOk (log: $state\logs\app.log)"
        $failed++
    }
}
Write-Output "$($screens.Count - $failed) of $($screens.Count) screens written. Pictures: $shots"
if ($failed -gt 0) { exit 1 }
exit 0
