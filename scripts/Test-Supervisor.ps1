# Supervisor exe check. The exe is now only the two read-only reports (review of 2026-11-15, section 4, Phase 2): the window is the one
# supervisor. This script checks that the exe:
#   - with no report, prints the usage, exits with 2 and returns at once (it does not supervise);
#   - writes nothing beside a state file it is given;
#   - runs --usage-report and --cache-report on an empty projects folder, and exits 0.
#
#   pwsh scripts\Test-Supervisor.ps1 [-Exe <path to OpenTail.Bullshark.Supervisor.exe>] [-Keep]
#
# Each check prints PASS or FAIL. The script exits with 1 if any check fails. -Keep leaves the scratch folder for a look.
param(
    [string]$Exe = ((@(Get-ChildItem -Path (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\OpenTail.Bullshark.Supervisor\bin\Debug\net1*\OpenTail.Bullshark.Supervisor.exe') -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -ExpandProperty FullName) + @(Join-Path (Split-Path -Parent $PSScriptRoot) 'src\OpenTail.Bullshark.Supervisor\bin\Debug\net11.0\OpenTail.Bullshark.Supervisor.exe'))[0]),
    [switch]$Keep
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Exe)) { throw "The exe is not built: $Exe (run dotnet build src\OpenTail.Bullshark.Supervisor first)" }

$work = Join-Path $env:TEMP ('bullshark-supervisor-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$state = Join-Path $work 'state.json'
$projects = Join-Path $work 'projects'
New-Item -ItemType Directory -Force $projects | Out-Null
$failed = 0

# Runs the exe with the arguments, waits at most the timeout, and returns the exit code and the text it printed.
function Invoke-Exe([string[]]$arguments, [int]$timeoutMs = 60000) {
    $id = [guid]::NewGuid().ToString('N').Substring(0, 6)
    $out = Join-Path $work "out-$id.txt"
    $err = Join-Path $work "err-$id.txt"
    $argLine = ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $process = Start-Process -FilePath $Exe -ArgumentList $argLine -NoNewWindow -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
    if (-not $process.WaitForExit($timeoutMs)) {
        $process.Kill()
        return @{ Finished = $false; Code = -1; Out = ''; Err = '' }
    }
    return @{ Finished = $true; Code = $process.ExitCode; Out = [string](Get-Content $out -Raw); Err = [string](Get-Content $err -Raw) }
}

function Check([string]$name, [bool]$ok, [string]$detail = '') {
    if ($ok) { Write-Host "PASS  $name" }
    else { Write-Host "FAIL  $name  $detail"; $script:failed++ }
}

$r = Invoke-Exe @()
Check 'no report: returns at once with exit 2' ($r.Finished -and $r.Code -eq 2) "exit $($r.Code), finished $($r.Finished)"
Check 'no report: says that it runs no supervisor' ($r.Err -match 'runs no supervisor') $r.Err

$r = Invoke-Exe @('--state', $state)
Check 'a state path and no report: exit 2' ($r.Finished -and $r.Code -eq 2) "exit $($r.Code)"
Check 'a state path: nothing is written beside it' (-not ((Test-Path $state) -or (Test-Path (Join-Path $work 'runs.json')) -or (Test-Path (Join-Path $work 'logs')))) 'a state, runs or logs file appeared'

$r = Invoke-Exe @('--usage-report', '--projects', $projects)
$report = if ($r.Code -eq 0) { $r.Out | ConvertFrom-Json } else { $null }
Check 'usage report on an empty folder: exit 0 and no files' ($r.Code -eq 0 -and $report.files -eq 0) "exit $($r.Code) $($r.Err)"

$r = Invoke-Exe @('--cache-report', '--projects', $projects)
Check 'cache report on an empty folder: exit 0' ($r.Finished -and $r.Code -eq 0) "exit $($r.Code) $($r.Err)"

if (-not $Keep) { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
if ($failed -gt 0) { Write-Host "$failed check(s) failed"; exit 1 }
Write-Host 'all checks passed'
