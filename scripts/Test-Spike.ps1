# P1 acceptance tests for the embedded terminal spike. These are live tests: they start small Haiku
# background sessions (about a cent in total) and remove them afterwards. Only sessions whose names start
# with "bullshark-test-" are touched; the owner's own sessions are never stopped or removed.
#
#   pwsh scripts\Test-Spike.ps1
#
# Each test prints PASS or FAIL. The script exits with 1 if any test fails.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repo 'src\OpenTail.Bullshark.App\bin\Debug\net10.0-windows\OpenTail.Bullshark.App.exe'
$work = Join-Path $env:TEMP ('bullshark-test-work-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $work | Out-Null
$results = New-Object System.Collections.Generic.List[object]

function Record($name, [bool]$ok, $detail) {
    $results.Add([pscustomobject]@{ Test = $name; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Detail = $detail })
}

function Get-Agent($name) {
    & claude agents --json | ConvertFrom-Json | Where-Object { $_.name -eq $name } | Select-Object -First 1
}

function Wait-State($name, [string[]]$states, $timeoutSec) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    do {
        $a = Get-Agent $name
        $s = if ($a) { if ($a.state) { $a.state } else { $a.status } } else { $null }
        if ($states -contains $s) { return $s }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    return $s
}

function Start-Bg($name, $prompt) {
    # `claude --bg` prints "backgrounded · <id> · <name>". stop and rm take the id (names do not work for them).
    $line = (& claude --bg --model haiku --effort low --name $name $prompt | Out-String)
    if ($line -notmatch 'backgrounded · ([0-9a-f]{8}) ·') { throw "could not start $name : $line" }
    return $matches[1]
}

function Run-App($command, $seconds, $name, $type, $typeAt) {
    $p = @{ Command = $command; Seconds = $seconds; Name = $name }
    if ($type) { $p.Type = $type; $p.TypeAt = $typeAt }
    & (Join-Path $repo 'scripts\Run-Spike.ps1') @p
}

$ids = @()
try {
    # T1: a child process runs inside the embedded terminal and its output is captured.
    $r = Run-App 'cmd.exe /c ping -n 4 127.0.0.1' 5 't1'
    Record 'T1 child output captured in embedded terminal' (([string]$r.Text) -match 'Reply from 127\.0\.0\.1') "exit=$($r.ExitCode)"

    # T2: launched from a console (redirected output), the child must not write to that console (finding F3).
    $consoleOut = Join-Path $work 'console.txt'
    $termOut = Join-Path $work 't2.txt'
    cmd.exe /c "`"$exe`" --smoke --log --seconds 5 --command `"cmd.exe /c ping -n 4 127.0.0.1`" --out `"$termOut`" >`"$consoleOut`" 2>&1"
    $consoleBytes = (Get-Item $consoleOut).Length
    $termText = Get-Content $termOut -Raw
    Record 'T2 no output leaks to the launching console' ($consoleBytes -eq 0 -and $termText -match 'Reply from') "console bytes=$consoleBytes"

    # Prepare one probe session for the attach tests.
    $probe = 'bullshark-test-probe-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
    Push-Location $work
    $probeId = Start-Bg $probe 'Reply with exactly the words PROBE OK and nothing else.'
    Pop-Location
    $ids += $probeId
    $state = Wait-State $probe @('done') 90
    Record 'setup probe session finishes' ($state -eq 'done') "state=$state"

    # T3: `claude attach` renders the real Claude Code screen inside the embedded terminal.
    $r = Run-App "claude attach $probe" 8 't3'
    $t = [string]$r.Text
    Record 'T3 claude attach renders the session in the terminal' (($t -match 'Claude Code') -and ($t -match 'PROBE OK')) "len=$($t.Length)"

    # T4: typing into the embedded tab reaches the attached session (the nudge path).
    $r = Run-App "claude attach $probe" 25 't4' 'Reply with exactly the words TYPED OK and nothing else.' 9
    $n = ([regex]::Matches([string]$r.Text, 'TYPED OK')).Count
    Record 'T4 typed input reaches the attached session' ($n -ge 2) "matches=$n (prompt echo + reply)"

    # T5: closing the tab leaves a running background session running.
    $long = 'bullshark-test-long-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
    Push-Location $work
    $longId = Start-Bg $long 'Use the Bash tool to run exactly: ping -n 25 127.0.0.1 > nul . Then reply with exactly the words LONG DONE.'
    Pop-Location
    $ids += $longId
    $null = Wait-State $long @('working') 30
    $r = Run-App "claude attach $long" 4 't5'
    $after = Wait-State $long @('working', 'done') 10
    Record 'T5 closing the tab does not stop the session' (@('working', 'done') -contains $after) "state after close=$after"
}
finally {
    foreach ($id in $ids) {
        # stop is asynchronous: wait for the session to reach done before removing it.
        & claude stop $id | Out-Null
        $deadline = (Get-Date).AddSeconds(20)
        while ((Get-Date) -lt $deadline -and ((& claude agents --json | ConvertFrom-Json | Where-Object { $_.id -eq $id }).state -eq 'working')) {
            Start-Sleep -Seconds 1
        }
        & claude rm $id | Out-Null
    }
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

$results | Format-Table -AutoSize | Out-String | Write-Host
if ($results | Where-Object { $_.Result -eq 'FAIL' }) { exit 1 } else { exit 0 }
