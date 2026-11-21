# Codex resume screen, screenshots (steps R7 of docs/plans/provider-codex-2026-11-21.md). A copy of the pattern of Test-NewScreens.ps1, for the two
# screens of the Codex resume dialog: "From the last turns" (dark) and "From a Codex summary" with Make summary pressed (light).
#
# Everything the App reads is a scratch fixture, built by this script in -Out:
#   - a state folder (the App's state.json and ui.json; ui.json picks Codex as the window's provider);
#   - a test .claude folder (empty) and a git repo (the folder the Codex session names);
#   - a Codex home (CODEX_HOME for the App process only) with one synthetic rollout: two exchanges and one injected context message;
#   - a stand-in codex.cmd (first on PATH for these runs only). It prints a fixed three-line summary and reads its prompt from standard input.
#     It is the only codex the App can reach here, so no real Codex session starts and no real codex exec call is made;
#   - the claude stand-in of Test-NewScreens.ps1 (it lists no sessions and starts nothing).
# The App is run with --no-pass, so no supervisor runs. --shot takes the picture of the resume dialog itself (the App's _shotDialog).
#
#   pwsh scripts\Test-CodexResume.ps1 -Exe <path to OpenTail.Bullshark.App.exe> [-Out <scratch folder>] [-Artifacts <artifacts folder>]
#
# -Exe is required: pass the artifacts-path build (the App is built for net11.0 only here, because Codex history needs net11.0: CodexProvider.Available).
# -Artifacts, when given, is the --artifacts-path of the fixture generator's build, so that build writes no default bin or obj folder.
# Each run prints PASS or FAIL. The App's log (in the state folder's logs folder) must show the step the screen names. The script exits 1 if any
# check fails. The PNGs are for a person to look at; they are not committed.
param(
    [Parameter(Mandatory)] [string]$Exe,
    [string]$Out = (Join-Path $env:TEMP 'bullshark-codexresume'),
    [string]$Artifacts = ''
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Exe)) { throw "The App is not built: $Exe" }
$Exe = (Resolve-Path -LiteralPath $Exe).Path
$repoRoot = Split-Path -Parent $PSScriptRoot
$core = (Resolve-Path -LiteralPath (Join-Path $repoRoot 'src\OpenTail.Bullshark.Core\OpenTail.Bullshark.Core.csproj')).Path

$state = Join-Path $Out 'state'
$claudeHome = Join-Path $Out 'home\.claude'
$codexHome = Join-Path $Out 'home\.codex'
$repo = Join-Path $Out 'repo'
$tools = Join-Path $Out 'tools'
$shots = Join-Path $Out 'shots'
# Only the folders this script makes are cleared; -Out itself is kept.
foreach ($folder in @($state, $claudeHome, $codexHome, $repo, $tools, $shots)) { if (Test-Path -LiteralPath $folder) { Remove-Item -Recurse -Force -LiteralPath $folder } }
New-Item -ItemType Directory -Force -Path $state, $claudeHome, $codexHome, $repo, $shots, "$tools\gen", "$tools\fakeclaude", "$tools\codexbin" | Out-Null

# The fixture generator: the Core library writes the state files, so their format is the App's own.
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
using OpenTail.Bullshark.Core.Supervisor;

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

# The claude stand-in: lists no sessions and starts nothing. It answers `daemon status` and `--version` only (as in Test-NewScreens.ps1).
@'
// A stand-in for the claude CLI during the Codex resume screenshots. It lists no sessions, attaches to nothing and starts nothing.
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

# The stand-in codex: `codex exec ...` reads its prompt from standard input (more >nul reads it all) and prints a fixed three-line summary. Any other
# call (such as --version) prints nothing and exits 0. Written without a byte-order mark, which cmd would read as part of the first line.
$codexCmd = @'
@echo off
if not "%~1"=="exec" exit /b 0
more >nul
echo Summary of the example session (stand-in, no model call).
echo The owner asked for the notes to be tidied, and the build passes.
echo The docs folder gets a short index of the notes files.
'@
[IO.File]::WriteAllText("$tools\codexbin\codex.cmd", $codexCmd, [Text.Encoding]::ASCII)

# The git repo: the folder the Codex session names. It needs to exist; its content is not read.
& git init -q $repo
Set-Content -LiteralPath "$repo\README.md" -Value "# Scratch repo`nNothing here is real.`n" -Encoding UTF8

# One synthetic Codex rollout, found by its file name (the session id ends it). The records are the shapes the reader reads: a session record that
# names the folder, a turn record, two exchanges, an injected context message (left out of the start text), and a tool call (not a message).
$sessionId = '019e2000-0000-7000-8000-00000000c0de'
$utf8 = [Text.UTF8Encoding]::new($false)
$records = @(
    @{ timestamp = '2026-11-21T09:00:00.000Z'; type = 'session_meta'; payload = @{ id = $sessionId; cwd = $repo; source = 'cli' } },
    @{ timestamp = '2026-11-21T09:00:01.000Z'; type = 'turn_context'; payload = @{ turn_id = 't1'; cwd = $repo; model = 'gpt-6-luna'; effort = 'medium' } },
    @{ timestamp = '2026-11-21T09:00:02.000Z'; type = 'response_item'; payload = @{ type = 'message'; role = 'user'; content = @(@{ type = 'input_text'; text = '<environment_context>synthetic context, not a message</environment_context>' }) } },
    @{ timestamp = '2026-11-21T09:00:03.000Z'; type = 'response_item'; payload = @{ type = 'message'; role = 'user'; content = @(@{ type = 'input_text'; text = 'Tidy the notes in notes.md and check that the build passes.' }) } },
    @{ timestamp = '2026-11-21T09:00:05.000Z'; type = 'response_item'; payload = @{ type = 'function_call'; name = 'shell'; arguments = '{"command":"dotnet build"}' } },
    @{ timestamp = '2026-11-21T09:00:07.000Z'; type = 'response_item'; payload = @{ type = 'message'; role = 'assistant'; content = @(@{ type = 'output_text'; text = 'The notes are tidy. The build passes with no warnings.' }) } },
    @{ timestamp = '2026-11-21T09:00:09.000Z'; type = 'response_item'; payload = @{ type = 'message'; role = 'user'; content = @(@{ type = 'input_text'; text = 'Now add a short index for the docs folder.' }) } },
    @{ timestamp = '2026-11-21T09:00:11.000Z'; type = 'response_item'; payload = @{ type = 'message'; role = 'assistant'; content = @(@{ type = 'output_text'; text = 'Added docs/index.md with a list of the notes files.' }) } }
)
$rolloutDir = Join-Path $codexHome 'sessions\2026\11\21'
New-Item -ItemType Directory -Force -Path $rolloutDir | Out-Null
$rolloutLines = $records | ForEach-Object { $_ | ConvertTo-Json -Compress -Depth 8 }
[IO.File]::WriteAllLines((Join-Path $rolloutDir "rollout-2026-11-21T09-00-00-$sessionId.jsonl"), [string[]]$rolloutLines, $utf8)

# Each screen: a theme for the window, the options that open the screen, and a name for its PNG. The ui.json names Codex as the provider, so the
# window reads the Codex history on start (the provider key of ProviderChoice).
$screens = @(
    @{ Name = 'codex-resume-tail-dark'; Theme = 'Dark'; Options = @('--codex-resume') },
    @{ Name = 'codex-resume-summary-light'; Theme = 'Light'; Options = @('--codex-resume', 'summary'); Summary = $true }
)

$originalPath = $env:PATH
$failed = 0
try {
    foreach ($screen in $screens) {
        $name = $screen.Name
        # The stand-ins go first on PATH for these runs only, so a codex or claude the App starts is a stand-in; CODEX_HOME is the scratch home.
        $env:PATH = "$tools\codexbin;$tools\fakebin;$originalPath"
        $env:CODEX_HOME = $codexHome
        $env:FAKECLAUDE_LOG = "$tools\calls.log"
        Set-Content -LiteralPath "$state\ui.json" -Value ('{"railTheme": "' + $screen.Theme + '", "provider": "codex"}') -Encoding UTF8
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
        # The App's log beside the state file: the history was read, and the step the screen names ran.
        $logPath = Join-Path $state 'logs\app.log'
        $log = if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Raw } else { '' }
        $historyOk = $log -match 'codex history read: 1 sessions'
        $stepOk = if ($screen.Summary) { $log -match 'codex summary made: \d' } else { $log -match 'codex start text built: \d' }
        if ($process.ExitCode -eq 0 -and $pictureOk -and $historyOk -and $stepOk) {
            Write-Output "PASS ${name}: exit 0, history read, the step ran, $png"
        } else {
            Write-Output "FAIL ${name}: exit $($process.ExitCode), picture written: $pictureOk, history read: $historyOk, step ran: $stepOk (log: $logPath)"
            $failed++
        }
    }
} finally {
    $env:PATH = $originalPath
    Remove-Item Env:CODEX_HOME -ErrorAction SilentlyContinue
}
Write-Output "$($screens.Count - $failed) of $($screens.Count) screens written. Pictures: $shots"
if ($failed -gt 0) { exit 1 }
exit 0
