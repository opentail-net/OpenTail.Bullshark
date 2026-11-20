# S3 acceptance: a hidden embedded terminal keeps its process, its output and its input (finding F45).
# The check uses a PowerShell counter, not a Claude session, so it costs nothing and touches no owner session.
# It runs the window with --no-pass and scratch paths, and the window closes itself after about 20 seconds.
#
#   pwsh scripts\Test-Pane.ps1 [-Exe <path to OpenTail.Bullshark.App.exe>]
#
# Each check prints PASS or FAIL. The script exits with 1 if any check fails.
param(
    [string]$Exe = ((@(Get-ChildItem -Path (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\OpenTail.Bullshark.App\bin\Debug\net1*-windows10.0.19041.0\OpenTail.Bullshark.App.exe') -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -ExpandProperty FullName) + @(Join-Path (Split-Path -Parent $PSScriptRoot) 'src\OpenTail.Bullshark.App\bin\Debug\net11.0-windows10.0.19041.0\OpenTail.Bullshark.App.exe'))[0])
)
$ErrorActionPreference = 'Stop'
$work = Join-Path $env:TEMP ('bullshark-pane-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$folder = Join-Path $work 'folder'
New-Item -ItemType Directory -Force $folder | Out-Null
$result = Join-Path $work 'result.json'

try {
    $args = @('--selftest', $result, '--no-pass', '--state', (Join-Path $work 'state.json'), '--workdir', $folder)
    $p = Start-Process -FilePath $Exe -ArgumentList $args -PassThru
    if (-not $p.WaitForExit(90000)) {
        $p.Kill()
        throw 'the self-test did not finish within 90 seconds'
    }
    if (-not (Test-Path $result)) { throw 'the self-test wrote no result file' }

    $r = Get-Content $result -Raw | ConvertFrom-Json
    $checks = @(
        [pscustomobject]@{ Test = 'the terminal process started';                    Result = [bool]$r.started },
        [pscustomobject]@{ Test = 'the process is alive after hide and show';        Result = [bool]$r.processAlive },
        [pscustomobject]@{ Test = 'output written while hidden (at least 5 ticks)';  Result = ($r.ticksWhileHidden -ge 5) },
        [pscustomobject]@{ Test = 'a keystroke reaches the terminal after show';     Result = [bool]$r.keyEcho }
    )
    $checks | ForEach-Object { '{0}  {1}' -f $(if ($_.Result) { 'PASS' } else { 'FAIL' }), $_.Test }
    if ($checks | Where-Object { -not $_.Result }) { exit 1 } else { exit 0 }
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
