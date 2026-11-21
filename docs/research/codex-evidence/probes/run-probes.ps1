# Codex evidence probes. See ../README.md for the rules (privacy, model calls, process hygiene).
# Without -IncludeModelCall nothing calls a model. The only model call is one "say hi" with gpt-6-luna.
param([switch]$IncludeModelCall)

$ErrorActionPreference = 'Continue'
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
$Evidence = Split-Path -Parent $Here
$Results = Join-Path $Evidence 'results'
$HelpDir = Join-Path $Results 'help'
$RepoRoot = (Resolve-Path (Join-Path $Evidence '..\..\..')).Path
New-Item -ItemType Directory -Force -Path $Results, $HelpDir | Out-Null
$Log = Join-Path $Results ((Get-Date -Format 'yyyy-MM-dd-HHmm') + '-run.md')

function Redact([string]$s) {
    if ($null -eq $s) { return '' }
    $s = [regex]::Replace($s, '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}', '<uuid>')
    $s = $s.Replace($env:USERPROFILE, '<home>')
    $s = [regex]::Replace($s, '(sk-|eyJ)[A-Za-z0-9._-]{10,}', '<secret>')
    return $s
}
function Add-Log([string]$text) { Add-Content -LiteralPath $Log -Value (Redact $text) -Encoding UTF8 }
function Section([string]$title) { Add-Log ''; Add-Log "## $title" }

# Header: the version under test, recorded in every run.
Add-Log "# Codex probe run $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
Add-Log ('- Codex: ' + ((& codex --version 2>&1 | Out-String).Trim()))
Add-Log ('- Repo commit: ' + ((git -C $RepoRoot rev-parse --short HEAD 2>&1 | Out-String).Trim()))
Add-Log ('- PowerShell ' + $PSVersionTable.PSVersion + ' on ' + [Environment]::OSVersion.VersionString)
Add-Log ('- Model calls allowed in this run: ' + [bool]$IncludeModelCall)

# P01 install
Section 'P01 install'
Add-Log ('codex resolves to: ' + (Redact (Get-Command codex -ErrorAction SilentlyContinue).Source))
Add-Log ('npm global: ' + ((npm ls -g '@openai/codex' --depth=0 2>&1 | Out-String).Trim()))

# P02 help snapshot (verbatim into results/help, one file per command)
Section 'P02 help snapshot'
$helpCommands = @(@(), @('exec'), @('resume'), @('queue'), @('agents'), @('app-server'), @('app-server', 'daemon'), @('remote-control'))
foreach ($c in $helpCommands) {
    $name = if ($c.Count) { $c -join '-' } else { 'root' }
    $text = & codex @c --help 2>&1 | Out-String
    Set-Content -LiteralPath (Join-Path $HelpDir "$name.txt") -Value $text -Encoding UTF8
    Add-Log ("saved help: $name ($($text.Length) characters)")
}

# P03 daemon and processes (read-only: nothing is started or stopped)
Section 'P03 daemon and codex processes (read-only)'
Add-Log ('daemon version: ' + (Redact ((& codex app-server daemon version 2>&1 | Out-String).Trim())))
$procs = Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match 'codex' }
foreach ($p in $procs) {
    $cl = $p.CommandLine
    $short = $cl.Substring(0, [Math]::Min(160, $cl.Length))
    Add-Log ('process {0} (parent {1}) {2}: {3}' -f $p.ProcessId, $p.ParentProcessId, $p.Name, (Redact $short))
}
if (-not $procs) { Add-Log 'no codex processes' }

# P04 read-only app-server calls. We start our own server and stop it (and only it) at the end.
Section 'P04 app-server, read-only methods'
$psi = [System.Diagnostics.ProcessStartInfo]::new('cmd.exe', '/d /c codex app-server')
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$srv = [System.Diagnostics.Process]::Start($psi)
$script:pending = $null
function Next-Line([int]$ms) {
    if ($null -eq $script:pending) { $script:pending = $srv.StandardOutput.ReadLineAsync() }
    if (-not $script:pending.Wait($ms)) { return $null }
    $line = $script:pending.Result
    $script:pending = $null
    return $line
}
function Send($obj) {
    $srv.StandardInput.WriteLine(($obj | ConvertTo-Json -Depth 20 -Compress))
    $srv.StandardInput.Flush()
}
function Recv([int]$id) {
    $deadline = [DateTime]::UtcNow.AddSeconds(25)
    while ([DateTime]::UtcNow -lt $deadline) {
        $line = Next-Line 2000
        if ($null -eq $line) { continue }
        try { $m = $line | ConvertFrom-Json } catch { continue }
        if ($m.id -eq $id) { return $m }
    }
    return $null
}
try {
    Send @{ jsonrpc = '2.0'; id = 1; method = 'initialize'; params = @{ clientInfo = @{ name = 'opentail-evidence'; version = '0.0.1' } } }
    $r = Recv 1
    Add-Log ('initialize: ' + $(if ($r.result) { 'ok' } else { 'no result' }))
    Send @{ jsonrpc = '2.0'; method = 'initialized' }

    Send @{ jsonrpc = '2.0'; id = 2; method = 'model/list'; params = @{ includeHidden = $true; limit = 100 } }
    $r = Recv 2
    foreach ($m in @($r.result.data)) {
        Add-Log ('model id={0} hidden={1} isDefault={2} displayName={3}' -f $m.id, $m.hidden, $m.isDefault, $m.displayName)
    }

    Send @{ jsonrpc = '2.0'; id = 3; method = 'thread/loaded/list'; params = @{} }
    $r = Recv 3
    Add-Log ('thread/loaded/list count: ' + @($r.result.data).Count)

    Send @{ jsonrpc = '2.0'; id = 4; method = 'thread/list'; params = @{ limit = 100 } }
    $r = Recv 4
    $threads = @($r.result.data)
    $statusCounts = $threads | ForEach-Object { [string]$_.status.type } | Group-Object | ForEach-Object { "$($_.Name)=$($_.Count)" }
    Add-Log ('thread/list count: ' + $threads.Count + '; status types: ' + ($statusCounts -join ', '))
    if ($threads.Count) { Add-Log ('thread field names: ' + (($threads[0].PSObject.Properties.Name) -join ', ')) }

    Send @{ jsonrpc = '2.0'; id = 5; method = 'account/rateLimits/read'; params = @{} }
    $r = Recv 5
    if ($r.result) {
        Add-Log ('account/rateLimits/read fields: ' + (($r.result.PSObject.Properties.Name) -join ', '))
        $p = $r.result.rateLimits.primary
        if ($p) { Add-Log ("primary window: usedPercent=$($p.usedPercent) windowDurationMins=$($p.windowDurationMins) resetsAt present=$([bool]$p.resetsAt)") }
    } else {
        Add-Log ('account/rateLimits/read: no result')
    }
} finally {
    try { $srv.StandardInput.Close() } catch {}
    if (-not $srv.WaitForExit(3000)) { taskkill /T /PID $srv.Id /F 2>&1 | Out-Null }
    Add-Log ("app-server started by this probe stopped (pid $($srv.Id))")
}

# P05 one "say hi" call (only with -IncludeModelCall). Event names and key names only; no message text.
Section 'P05 say hi with gpt-6-luna (model call)'
if (-not $IncludeModelCall) {
    Add-Log 'skipped: run with -IncludeModelCall to make the one call'
} else {
    $freeMb = [math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1024)
    Add-Log "free RAM before the call: $freeMb MB"
    $promptFile = Join-Path $env:TEMP 'codex-evidence-say-hi.txt'
    Set-Content -LiteralPath $promptFile -Value 'say hi' -Encoding ASCII
    Push-Location $RepoRoot
    $t0 = Get-Date
    $out = cmd.exe /d /c "codex exec -m gpt-6-luna --ephemeral -s read-only --json < `"$promptFile`"" 2>&1 | Out-String
    $exit = $LASTEXITCODE
    Pop-Location
    Remove-Item -LiteralPath $promptFile -ErrorAction SilentlyContinue
    Add-Log ("exit code: $exit; seconds: " + [math]::Round(((Get-Date) - $t0).TotalSeconds, 1))
    $events = @()
    foreach ($line in ($out -split "`r?`n")) {
        $t = $line.Trim()
        if (-not $t.StartsWith('{')) {
            if ($t -match '^ERROR') { Add-Log ('error line: ' + (Redact $t.Substring(0, [Math]::Min(200, $t.Length)))) }
            continue
        }
        try { $events += ($t | ConvertFrom-Json) } catch { }
    }
    Add-Log ('JSON events: ' + $events.Count)
    $i = 0
    foreach ($e in $events) {
        $i++
        Add-Log ('event {0}: keys=[{1}]' -f $i, (($e.PSObject.Properties.Name) -join ','))
    }
}

Write-Output "Wrote $Log"
