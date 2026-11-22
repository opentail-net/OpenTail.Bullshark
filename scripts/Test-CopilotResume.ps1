# Copilot resume screen, screenshots (stage G3.3 of docs/plans/provider-copilot-2026-11-22.md). A copy of the pattern of Test-CodexResume.ps1 for the
# Copilot resume dialog: each of the three ways to continue (In full, From the last turns, From a Copilot summary), in light and dark.
#
# Everything the App reads is a scratch fixture, built by this script in -Out:
#   - a state folder (the App's state.json and ui.json; ui.json picks Copilot as the window's provider);
#   - a Copilot home (COPILOT_HOME for the App process only) with one synthetic session: workspace.yaml and events.jsonl, two exchanges;
#   - the session's folder (an empty folder; its content is not read);
#   - a stand-in copilot.cmd (first on PATH for these runs only). With -p it prints a fixed three-line summary, so the summary mode makes its call to
#     the stand-in and no model call. Any other call prints nothing and exits 0;
#   - a stand-in copilot.ps1 (the shim the start tab would run). It only writes its arguments to a log file. No screen here presses Start.
# The App is run with --no-pass, so no supervisor runs. --shot takes the picture of the resume dialog itself (the App's _shotDialog).
#
# Run it only when no Bullshark window and no devenv are running, and when at least 8 GB of RAM is free: the owner's windows are not touched
# (the App is started with its state file in -Out only). This script was written in stage G3.3 and was NOT run when it was written (devenv was open).
#
#   pwsh scripts\Test-CopilotResume.ps1 -Exe <path to OpenTail.Bullshark.App.exe> [-Out <scratch folder>] [-Artifacts <artifacts folder>]
#
# -Exe is required. -Artifacts, when given, is the --artifacts-path of the fixture generator's build, so that build writes no default bin or obj folder.
# Each run prints PASS or FAIL. The App's log (in the state folder's logs folder) must show the step the screen names. The script exits 1 if any check
# fails. The PNGs are for a person to look at; they are not committed. The summary call itself goes through cmd.exe to the stand-in, so the real route
# (cmd.exe to the copilot shim) is checked only by its shape here, not by a real call (owner check R-C9).
param(
    [Parameter(Mandatory)] [string]$Exe,
    [string]$Out = (Join-Path $env:TEMP 'bullshark-copilotresume'),
    [string]$Artifacts = ''
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Exe)) { throw "The App is not built: $Exe" }
$Exe = (Resolve-Path -LiteralPath $Exe).Path
$repoRoot = Split-Path -Parent $PSScriptRoot
$core = (Resolve-Path -LiteralPath (Join-Path $repoRoot 'src\OpenTail.Bullshark.Core\OpenTail.Bullshark.Core.csproj')).Path

$state = Join-Path $Out 'state'
$claudeHome = Join-Path $Out 'home\.claude'
$copilotHome = Join-Path $Out 'home\.copilot'
$folder = Join-Path $Out 'session-folder'
$tools = Join-Path $Out 'tools'
$shots = Join-Path $Out 'shots'
# Only the folders this script makes are cleared; -Out itself is kept.
foreach ($item in @($state, $claudeHome, $copilotHome, $folder, $tools, $shots)) { if (Test-Path -LiteralPath $item) { Remove-Item -Recurse -Force -LiteralPath $item } }
New-Item -ItemType Directory -Force -Path $state, $claudeHome, $copilotHome, $folder, $shots, "$tools\gen", "$tools\fakeclaude", "$tools\copilotbin" | Out-Null

# The fixture generator: the Core library writes the state files, so their format is the App's own (as in Test-CodexResume.ps1).
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
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Library;

// Scratch state for the screenshots: no run and no record, so the state files only say so. Usage: gen <stateFolder>.
var stateFolder = args[0];
Directory.CreateDirectory(stateFolder);
new RunStore(Path.Combine(stateFolder, "runs.json")).Save([]);
new StateStore(Path.Combine(stateFolder, "state.json")).Save(new Dictionary<string, SessionRecord>(StringComparer.Ordinal));
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

# The claude stand-in: lists no sessions and starts nothing (as in Test-CodexResume.ps1).
@'
// A stand-in for the claude CLI during the Copilot resume screenshots. It lists no sessions, attaches to nothing and starts nothing.
var callLog = Environment.GetEnvironmentVariable("FAKECLAUDE_LOG");
if (!string.IsNullOrEmpty(callLog)) File.AppendAllText(callLog, string.Join(' ', args) + "\n");
if (args.Length > 0 && args[0] == "agents") Console.Out.Write("[]");
else if (args.Length >= 2 && args[0] == "daemon" && args[1] == "status")
    Console.Out.Write("running (pid 4312, version 2.1.326, uptime 2h 5m)\n");
else if (args.Length > 0 && args[0] == "--version") Console.Out.Write("2.1.326 (Claude Code)\n");
return 0;
'@ | Set-Content -LiteralPath "$tools\fakeclaude\Program.cs" -Encoding UTF8

& dotnet build "$tools\gen\gen.csproj" -o "$tools\gen\out" $(if ($Artifacts) { '--artifacts-path', $Artifacts } else { @() }) | Out-Null
if ($LASTEXITCODE -ne 0) { throw "The fixture generator did not build (see dotnet build output)" }
& dotnet build "$tools\fakeclaude\fakeclaude.csproj" -o "$tools\fakebin" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "The stand-in for claude did not build" }
& dotnet "$tools\gen\out\gen.dll" $state
if ($LASTEXITCODE -ne 0) { throw "The fixture generator failed" }

# The stand-in copilot.cmd: `copilot -p ...` (the summary call, through cmd.exe) prints a fixed three-line summary; any other call exits 0 and prints
# nothing. Written without a byte-order mark, which cmd would read as part of the first line. No parentheses in the echo lines (cmd blocks).
$copilotCmd = @'
@echo off
if not "%~1"=="-p" exit /b 0
echo Summary of the example session, stand-in, no model call.
echo The owner asked for the notes to be tidied, and the build passes.
echo The docs folder gets a short index of the notes files.
exit /b 0
'@
[IO.File]::WriteAllText("$tools\copilotbin\copilot.cmd", $copilotCmd, [Text.Encoding]::ASCII)

# The stand-in copilot.ps1: the shim the start tab would run. It records its arguments in the log named by FAKECOPILOT_LOG and starts nothing else.
@'
if ($env:FAKECOPILOT_LOG) { Add-Content -LiteralPath $env:FAKECOPILOT_LOG -Value ($args -join ' ') }
exit 0
'@ | Set-Content -LiteralPath "$tools\copilotbin\copilot.ps1" -Encoding UTF8

# The synthetic Copilot session: a folder under session-state named by its id, with workspace.yaml and events.jsonl. The records are the shapes the reader
# reads: a session.start that names the id, the start time and the working folder; two exchanges (user and assistant messages); turn ends. The text is
# made up and nothing here is read from a real session.
$sessionId = '2b7e1c20-0000-4000-8000-00000000c0de'
$utf8 = [Text.UTF8Encoding]::new($false)
$sessionDir = Join-Path $copilotHome "session-state\$sessionId"
New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null
$workspace = @(
    "id: $sessionId",
    "cwd: `"$folder`"",
    'name: Example Copilot session',
    'user_named: true',
    'created_at: 2026-11-21T09:00:00.000Z',
    'updated_at: 2026-11-21T09:00:11.000Z'
)
[IO.File]::WriteAllLines((Join-Path $sessionDir 'workspace.yaml'), [string[]]$workspace, $utf8)
$events = @(
    @{ type = 'session.start'; data = @{ sessionId = $sessionId; startTime = '2026-11-21T09:00:00.000Z'; selectedModel = 'example-model'; context = @{ cwd = $folder } } },
    @{ type = 'user.message'; data = @{ content = 'Tidy the notes in notes.md and check that the build passes.' } },
    @{ type = 'assistant.message'; data = @{ content = 'The notes are tidy. The build passes with no warnings.' } },
    @{ type = 'assistant.turn_end'; data = @{} },
    @{ type = 'user.message'; data = @{ content = 'Now add a short index for the docs folder.' } },
    @{ type = 'assistant.message'; data = @{ content = 'Added docs/index.md with a list of the notes files.' } },
    @{ type = 'assistant.turn_end'; data = @{} }
)
$eventLines = $events | ForEach-Object { $_ | ConvertTo-Json -Compress -Depth 6 }
[IO.File]::WriteAllLines((Join-Path $sessionDir 'events.jsonl'), [string[]]$eventLines, $utf8)

# Each screen: a theme for the window, the option that opens the screen (and its mode), a name for its PNG, and the log line the step must show.
$screens = @(
    @{ Name = 'copilot-resume-last-turns-dark'; Theme = 'Dark'; Mode = ''; Expect = 'copilot start text built: \d' },
    @{ Name = 'copilot-resume-last-turns-light'; Theme = 'Light'; Mode = ''; Expect = 'copilot start text built: \d' },
    @{ Name = 'copilot-resume-full-dark'; Theme = 'Dark'; Mode = 'full'; Expect = 'copilot start text built: \d' },
    @{ Name = 'copilot-resume-full-light'; Theme = 'Light'; Mode = 'full'; Expect = 'copilot start text built: \d' },
    # The summary mode: Make summary is pressed once; the stand-in answers; the summary is kept as a file beside the state file.
    @{ Name = 'copilot-resume-summary-dark'; Theme = 'Dark'; Mode = 'summary'; Expect = 'copilot summary made: \d' },
    @{ Name = 'copilot-resume-summary-light'; Theme = 'Light'; Mode = 'summary'; Expect = 'copilot summary made: \d' }
)

$originalPath = $env:PATH
$originalHome = $env:COPILOT_HOME
$failed = 0
try {
    foreach ($screen in $screens) {
        $name = $screen.Name
        # The stand-ins go first on PATH for these runs only, so a copilot the App starts is a stand-in; COPILOT_HOME is the scratch home.
        $env:PATH = "$tools\copilotbin;$tools\fakebin;$originalPath"
        $env:COPILOT_HOME = $copilotHome
        $env:FAKECOPILOT_LOG = "$tools\copilot-calls.log"
        $env:FAKECLAUDE_LOG = "$tools\calls.log"
        Set-Content -LiteralPath "$state\ui.json" -Value ('{"railTheme": "' + $screen.Theme + '", "provider": "copilot"}') -Encoding UTF8
        Set-Content -LiteralPath "$state\heartbeat.json" -Value ('{"at": "' + (Get-Date).ToUniversalTime().ToString('o') + '", "pass": 7}') -Encoding UTF8
        $png = Join-Path $shots "$name.png"
        $argList = @('--state', "$state\state.json", '--no-pass', '--workdir', $folder, '--claude-home', $claudeHome,
            '--shot', $png, '--shot-after', '15', '--copilot-resume')
        if ($screen.Mode) { $argList += $screen.Mode }
        $process = Start-Process -FilePath $Exe -ArgumentList $argList -PassThru -WorkingDirectory $folder
        if (-not $process.WaitForExit(60000)) {
            Stop-Process -Id $process.Id -Force
            Write-Output "FAIL ${name}: the window did not close within 60 s (it was stopped)"
            $failed++
            continue
        }
        $pictureOk = (Test-Path -LiteralPath $png) -and ((Get-Item -LiteralPath $png).Length -gt 0)
        # The App's log beside the state file: the history was read, and the step the screen names ran.
        $logPath = Join-Path $state 'logs\app.log'
        $log = if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Raw } else { '' }
        $historyOk = $log -match 'copilot history read: 1 sessions'
        $stepOk = $log -match $screen.Expect
        # A summary screen must keep its file: the summary is saved beside the state file (the log holds the count only).
        $savedOk = if ($screen.Mode -eq 'summary') { $log -match 'copilot summary saved as a file: \d' } else { $true }
        # The stand-in shim is never run by a screen (no Start is pressed here): its log must not exist.
        $noStartOk = -not (Test-Path -LiteralPath "$tools\copilot-calls.log")
        if ($process.ExitCode -eq 0 -and $pictureOk -and $historyOk -and $stepOk -and $savedOk -and $noStartOk) {
            Write-Output "PASS ${name}: exit 0, history read, the step ran, $png"
        } else {
            Write-Output "FAIL ${name}: exit $($process.ExitCode), picture written: $pictureOk, history read: $historyOk, step ran: $stepOk, summary saved: $savedOk, no start: $noStartOk (log: $logPath)"
            $failed++
        }
    }
} finally {
    $env:PATH = $originalPath
    if ($null -eq $originalHome) { Remove-Item Env:COPILOT_HOME -ErrorAction SilentlyContinue } else { $env:COPILOT_HOME = $originalHome }
    Remove-Item Env:FAKECOPILOT_LOG, Env:FAKECLAUDE_LOG -ErrorAction SilentlyContinue
}
Write-Output "$($screens.Count - $failed) of $($screens.Count) screens written. Pictures: $shots"
if ($failed -gt 0) { exit 1 }
exit 0
