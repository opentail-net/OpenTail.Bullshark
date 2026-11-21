# Claude read-only probes for the evidence library. No model calls, nothing started or stopped.
# Logs counts, states and key names only: no session names, ids, folders or messages.
$ErrorActionPreference = 'Continue'
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
$Evidence = Split-Path -Parent $Here
$Results = Join-Path $Evidence 'results'
New-Item -ItemType Directory -Force -Path $Results | Out-Null
$Log = Join-Path $Results ((Get-Date -Format 'yyyy-MM-dd-HHmm') + '-claude-run.md')

function Redact([string]$s) {
    if ($null -eq $s) { return '' }
    $s = [regex]::Replace($s, '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}', '<uuid>')
    $s = $s.Replace($env:USERPROFILE, '<home>')
    return $s
}
function Add-Log([string]$text) { Add-Content -LiteralPath $Log -Value (Redact $text) -Encoding UTF8 }

Add-Log "# Claude probe run $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
Add-Log ('- Claude: ' + ((& claude --version 2>&1 | Out-String).Trim()))

Add-Log ''
Add-Log '## P06 daemon status (read-only)'
Add-Log (Redact ((& claude daemon status 2>&1 | Out-String).Trim()))

Add-Log ''
Add-Log '## P07 background sessions, states only (read-only, --json --all)'
$raw = & claude agents --json --all 2>&1 | Out-String
$json = $null
try { $json = $raw | ConvertFrom-Json } catch { Add-Log ('not JSON; length ' + $raw.Length) }
if ($json) {
    $items = @($json)
    if ($json.PSObject.Properties.Name -contains 'agents') { $items = @($json.agents) }
    if ($json.PSObject.Properties.Name -contains 'sessions') { $items = @($json.sessions) }
    Add-Log ('top-level type: ' + $json.GetType().Name + '; items: ' + $items.Count)
    if ($items.Count) {
        Add-Log ('item key names: ' + (($items[0].PSObject.Properties.Name) -join ', '))
        $states = $items | ForEach-Object { [string]$_.state } | Group-Object | ForEach-Object { "$($_.Name)=$($_.Count)" }
        Add-Log ('state counts: ' + ($states -join ', '))
        $statuses = $items | ForEach-Object { [string]$_.status } | Group-Object | ForEach-Object { "$($_.Name)=$($_.Count)" }
        if ($statuses) { Add-Log ('status counts: ' + ($statuses -join ', ')) }
    }
}

Add-Log ''
Add-Log '## P07b same listing without --all (states only)'
$raw2 = & claude agents --json 2>&1 | Out-String
try {
    $j2 = $raw2 | ConvertFrom-Json
    $i2 = @($j2); if ($j2.PSObject.Properties.Name -contains 'agents') { $i2 = @($j2.agents) }
    Add-Log ('items without --all: ' + $i2.Count + '; states: ' + (($i2 | ForEach-Object { [string]$_.state } | Group-Object | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ', '))
} catch { Add-Log 'not JSON without --all' }

Write-Output "Wrote $Log"
