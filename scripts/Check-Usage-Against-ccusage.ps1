# Compares OpenTail.Bullshark's local usage totals with ccusage 15.1.0 on the same transcripts (F35, F36).
# Both read files only. ccusage is installed into a scratch folder under %TEMP%, not into the product.
#
#   pwsh scripts\Check-Usage-Against-ccusage.ps1
#
# Expected: input, cache-write and cache-read totals are identical. Output differs by a small amount, because
# OpenTail.Bullshark keeps the largest count per message (the placeholder rule, F36) and ccusage keeps the first.
param([string] $CcusageVersion = '15.1.0')

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path $env:TEMP 'bullshark-ccusage-check'
New-Item -ItemType Directory -Force $scratch | Out-Null

$ccBin = Join-Path $scratch "node_modules\.bin\ccusage.cmd"
if (-not (Test-Path $ccBin)) {
    Push-Location $scratch
    npm install --silent --no-audit --no-fund "ccusage@$CcusageVersion" | Out-Null
    Pop-Location
}

# Run both now, one after the other, so the transcripts are the same for both.
$cc = (& $ccBin session --json --offline 2>$null | Out-String) | ConvertFrom-Json
$ccTotals = [ordered]@{ input = 0; output = 0; cacheWrite = 0; cacheRead = 0 }
foreach ($s in $cc.sessions) {
    $ccTotals.input += $s.inputTokens
    $ccTotals.output += $s.outputTokens
    $ccTotals.cacheWrite += $s.cacheCreationTokens
    $ccTotals.cacheRead += $s.cacheReadTokens
}

$exe = (@(Get-ChildItem -Path (Join-Path $repo 'src\OpenTail.Bullshark.Supervisor\bin\Debug\net1*\OpenTail.Bullshark.Supervisor.dll') -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -ExpandProperty FullName) + @(Join-Path $repo 'src\OpenTail.Bullshark.Supervisor\bin\Debug\net11.0\OpenTail.Bullshark.Supervisor.dll'))[0]
if (-not (Test-Path $exe)) { throw "Build the supervisor first: $exe not found" }
$ours = (& dotnet $exe --usage-report | Out-String) | ConvertFrom-Json
$ourTotals = [ordered]@{ input = $ours.inputTokens; output = $ours.outputTokens; cacheWrite = $ours.cacheCreationTokens; cacheRead = $ours.cacheReadTokens }

$rows = foreach ($k in 'input', 'output', 'cacheWrite', 'cacheRead') {
    $diff = $ourTotals[$k] - $ccTotals[$k]
    $pct = if ($ccTotals[$k] -eq 0) { 0 } else { [math]::Round(100.0 * $diff / $ccTotals[$k], 4) }
    [pscustomobject]@{ Metric = $k; OpenTail.Bullshark = $ourTotals[$k]; ccusage = $ccTotals[$k]; Difference = $diff; Percent = $pct }
}
$rows | Format-Table -AutoSize | Out-String | Write-Host
"transcript files read by OpenTail.Bullshark: $($ours.files); ccusage sessions: $(@($cc.sessions).Count)"

$exact = ($rows | Where-Object { $_.Metric -ne 'output' } | Where-Object { $_.Difference -ne 0 }).Count -eq 0
$outputClose = [math]::Abs(($rows | Where-Object Metric -eq 'output').Percent) -lt 0.5
if ($exact -and $outputClose) { 'RESULT: consistent with ccusage (input, cache exact; output within 0.5%)'; exit 0 }
'RESULT: DIFFERENT - investigate (see table above)'
exit 1
