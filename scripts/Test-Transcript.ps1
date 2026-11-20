# Transcript view, TA4 check. Runs the built App on a scratch state folder with --no-pass, opens one past session's transcript with
# --open-transcript, and takes a screenshot. It makes no click. The checks:
#   - the app exits 0 (it closes itself after the screenshot);
#   - the screenshot is written;
#   - the log has no "turn facts not read" line and no "No transcript for session" line.
# The session is one of the owner's own transcripts: the smallest one with a failed tool call and a turn_duration record, unless
# -SessionId names another. The screenshot shows the owner's conversation, so it is for the owner to look at and then delete.
# The facts of a turn appear on its "turn finished" line; a failed call is drawn in red. Both are seen by eye in the screenshot.
#
#   pwsh scripts\Test-Transcript.ps1 [-SessionId <id or its start>] [-Exe <path to OpenTail.Bullshark.App.exe>] [-Zoom <percent>] [-Keep]
#
# Each check prints PASS or FAIL. The script exits with 1 if any check fails. -Keep leaves the scratch folder for a look.
param(
    [string]$SessionId = '',
    [string]$Exe = ((@(Get-ChildItem -Path (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\OpenTail.Bullshark.App\bin\Debug\net1*-windows10.0.19041.0\OpenTail.Bullshark.App.exe') -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -ExpandProperty FullName) + @(Join-Path (Split-Path -Parent $PSScriptRoot) 'src\OpenTail.Bullshark.App\bin\Debug\net11.0-windows10.0.19041.0\OpenTail.Bullshark.App.exe'))[0]),
    [string]$Shots = (Join-Path $env:TEMP 'bullshark-transcript-shots'),
    [int]$Zoom = 0,
    [switch]$Keep
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Exe)) { throw "The App is not built: $Exe (run dotnet build src\OpenTail.Bullshark.App first)" }

# The smallest non-subagent transcript that has a failed tool call and a turn_duration record.
function Find-Transcript {
    $root = Join-Path $env:USERPROFILE '.claude\projects'
    foreach ($file in (Get-ChildItem $root -Recurse -Filter *.jsonl | Where-Object { $_.FullName -notmatch '\\subagents\\' } | Sort-Object Length)) {
        $text = [System.IO.File]::ReadAllText($file.FullName)
        if ($text.Contains('"is_error":true') -and $text.Contains('"subtype":"turn_duration"')) { return $file.FullName }
    }
    return $null
}

if ($SessionId -eq '') {
    $transcript = Find-Transcript
    if ($null -eq $transcript) { throw 'No transcript with a failed tool call and a turn_duration record was found.' }
    $SessionId = [System.IO.Path]::GetFileNameWithoutExtension($transcript)
}

New-Item -ItemType Directory -Force $Shots | Out-Null
$work = Join-Path $env:TEMP ('bullshark-transcript-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $work | Out-Null
# -Zoom starts the window at a zoom: the saved value in ui.json, which the window reads at start (zoom.md).
if ($Zoom -gt 0) { Set-Content -Path (Join-Path $work 'ui.json') -Value ('{ "zoomPercent": ' + $Zoom + ' }') }
$state = Join-Path $work 'state.json'
$shot = Join-Path $Shots ("transcript-" + $SessionId.Substring(0, [Math]::Min(8, $SessionId.Length)) + ".png")
Remove-Item $shot -ErrorAction SilentlyContinue
$failed = 0

function Check([string]$name, [bool]$ok, [string]$detail = '') {
    if ($ok) { Write-Host "PASS  $name" }
    else { Write-Host "FAIL  $name  $detail"; $script:failed++ }
}

$appArgs = @('--no-pass', '--state', $state, '--open-transcript', $SessionId, '--shot', $shot, '--shot-after', '10')
$process = Start-Process -FilePath $Exe -ArgumentList $appArgs -PassThru
$finished = $process.WaitForExit(150000)
if (-not $finished) { $process.Kill() }

Check 'the app exits 0 after the screenshot' ($finished -and $process.ExitCode -eq 0) "finished $finished, exit $($process.ExitCode)"
Check 'the screenshot is written' (Test-Path $shot) $shot

$log = Join-Path $work 'logs\app.log'
$logText = if (Test-Path $log) { [string](Get-Content $log -Raw) } else { '' }
Check 'the log has no "turn facts not read" line' (-not ($logText -match 'turn facts not read')) $logText
Check 'the session was found in the history' ($logText -match "open transcript: " -and -not ($logText -match 'open transcript: no session')) "session $SessionId"

Write-Host "screenshot: $shot"
if (-not $Keep) { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
if ($failed -gt 0) { Write-Host "$failed check(s) failed"; exit 1 }
Write-Host 'all checks passed'
