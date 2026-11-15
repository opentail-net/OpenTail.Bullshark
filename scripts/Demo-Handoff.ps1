# End-to-end demo of the handoff: the supervisor starts the planner and the coder, the planner sends the coder
# a batch with SendMessage, the coder does it and reports back with SendMessage. Nothing is passed through files.
#
#   pwsh scripts\Demo-Handoff.ps1 [-TimeoutSec 900]
#
# While it runs you can watch either session: `claude attach ovc-planner` or `claude attach ovc-coder`.
# The sessions are stopped and removed at the end. The demo folder is samples\demo-handoff.
param([int] $TimeoutSec = 900)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$demo = Join-Path $repo 'samples\demo-handoff'
$hello = Join-Path $demo 'hello.txt'
$stateDir = Join-Path $env:TEMP 'bullshark-demo'
$state = Join-Path $stateDir 'state.json'
New-Item -ItemType Directory -Force $demo, $stateDir | Out-Null
Remove-Item $hello, $state -ErrorAction SilentlyContinue

$task = 'Create a file named hello.txt in this folder that contains exactly the line: hello from the coder'
& dotnet run --project (Join-Path $repo 'src\OpenTail.Bullshark.Supervisor') -- --workdir $demo --task $task --once --state $state

function Get-Peer($name) { & claude agents --json | ConvertFrom-Json | Where-Object { $_.name -eq $name } | Select-Object -First 1 }

$deadline = (Get-Date).AddSeconds($TimeoutSec)
$finished = $false
while ((Get-Date) -lt $deadline) {
    $p = Get-Peer 'ovc-planner'
    $c = Get-Peer 'ovc-coder'
    if ((Test-Path $hello) -and $p -and $c -and $p.state -eq 'done' -and $c.state -eq 'done') { $finished = $true; break }
    Start-Sleep -Seconds 10
}

"finished: $finished"
if (Test-Path $hello) { "hello.txt: " + (Get-Content $hello -Raw).Trim() } else { 'hello.txt: not created' }

# Evidence that the handoff used messages: count SendMessage calls in each session's transcript.
foreach ($name in 'ovc-planner', 'ovc-coder') {
    $peer = Get-Peer $name
    if ($peer) {
        $file = Get-ChildItem (Join-Path $env:USERPROFILE '.claude\projects') -Recurse -Filter "$($peer.sessionId).jsonl" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($file) {
            $sends = (Select-String -Path $file.FullName -Pattern '"name":"SendMessage"' -AllMatches).Count
            "$name transcript: $sends SendMessage call(s)"
        }
    }
}

# Clean up the demo sessions. Only these two names are touched.
foreach ($name in 'ovc-planner', 'ovc-coder') {
    $peer = Get-Peer $name
    if ($peer) {
        & claude stop $peer.id | Out-Null
        $d = (Get-Date).AddSeconds(30)
        while ((Get-Date) -lt $d -and (Get-Peer $name).state -eq 'working') { Start-Sleep -Seconds 2 }
        & claude rm $peer.id | Out-Null
    }
}
"sessions left: " + ((& claude agents --json | ConvertFrom-Json | ForEach-Object { $_.name }) -join ', ')
