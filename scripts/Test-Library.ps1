# Session library, L4 check. Runs the built App on scratch state folders with --no-pass, and checks what can be read without a click:
#   - a seeded library.json is shown: the run exits, and it writes a screenshot;
#   - a run does not change library.json (startup only reads it);
#   - the light theme and the Archived filter run and write a screenshot;
#   - an unreadable library.json is reported in the log and left as it was (D-L3).
# The screenshots are for a person to look at, one per theme. Menus, the edit dialog, typing and hovers need clicks, and this
# script makes none of them (docs/plans/session-library.md, L4).
#
#   pwsh scripts\Test-Library.ps1 [-Exe <path to OpenTail.Bullshark.App.exe>] [-Keep]
#
# Each check prints PASS or FAIL. The script exits with 1 if any check fails. -Keep leaves the scratch folder for a look.
param(
    [string]$Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\OpenTail.Bullshark.App\bin\Debug\net10.0-windows\OpenTail.Bullshark.App.exe'),
    [string]$Shots = (Join-Path $env:TEMP 'bullshark-library-shots'),
    [switch]$Keep
)
$ErrorActionPreference = 'Stop'
# The screenshots go to -Shots, which is kept; the scratch state folders are removed unless -Keep is given.
New-Item -ItemType Directory -Force $Shots | Out-Null
$work = Join-Path $env:TEMP ('bullshark-library-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$folder = Join-Path $work 'folder'
New-Item -ItemType Directory -Force $folder | Out-Null

# Three past sessions by id: one pinned and named with a tag, one archived, one with a tag and a colour.
$seed = @'
{
  "29b84a3b-6b18-419e-8d46-19cbd044edb9": { "Name": "Plan for L4", "Pinned": true, "Archived": false, "Tags": ["plan", "review"], "Note": "pinned for the library check", "Colour": "Blue" },
  "f8045ed8-4c12-4c65-8ab6-de7908490b71": { "Name": null, "Pinned": false, "Archived": true, "Tags": ["old"], "Note": null, "Colour": null },
  "17a94483-99fc-4005-9fa9-dbd63958653d": { "Name": null, "Pinned": false, "Archived": false, "Tags": ["compare"], "Note": null, "Colour": "Red" }
}
'@
$broken = '{ "29b84a3b-6b18-419e-8d46-19cbd044edb9": { "Name": "Broken'
$utf8 = [System.Text.UTF8Encoding]::new($false)
$results = New-Object System.Collections.Generic.List[object]

function Add-Check([string]$name, [bool]$ok, [string]$detail) {
    $results.Add([pscustomobject]@{ Check = $name; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Detail = $detail })
}

# Runs the app once on a state folder and returns its exit code (-1 when it had to be killed).
function Invoke-App([string]$state, [string[]]$extra, [string]$shot) {
    $appArgs = @('--no-pass', '--state', $state, '--workdir', $folder, '--shot', $shot, '--shot-after', '7') + $extra
    $process = Start-Process -FilePath $Exe -ArgumentList $appArgs -PassThru
    if (-not $process.WaitForExit(60000)) { $process.Kill(); return -1 }
    return $process.ExitCode
}

try {
    $env:OPENTAIL_BULLSHARK_EXPAND = '1'

    # 1 and 2: the seeded library is shown, and the run does not change it.
    $dark = Join-Path $work 'dark'
    New-Item -ItemType Directory -Force $dark | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $dark 'library.json'), $seed.Replace("`r`n", "`n"), $utf8)
    $before = (Get-FileHash (Join-Path $dark 'library.json')).Hash
    $exit = Invoke-App (Join-Path $dark 'state.json') @('--rail-filter', 'all') (Join-Path $Shots 'dark.png')
    Add-Check 'dark: the seeded library is shown, and the app exits and writes a screenshot' ($exit -eq 0 -and (Test-Path (Join-Path $Shots 'dark.png'))) "exit $exit; screenshot $Shots\dark.png"
    Add-Check 'a run leaves library.json as it was' ((Get-FileHash (Join-Path $dark 'library.json')).Hash -eq $before) 'startup only reads the file'

    # 3: the light theme (ui.json beside the state file).
    $light = Join-Path $work 'light'
    New-Item -ItemType Directory -Force $light | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $light 'library.json'), $seed.Replace("`r`n", "`n"), $utf8)
    [System.IO.File]::WriteAllText((Join-Path $light 'ui.json'), '{"railTheme":"Light"}', $utf8)
    $exit = Invoke-App (Join-Path $light 'state.json') @('--rail-filter', 'all') (Join-Path $Shots 'light.png')
    Add-Check 'light: the app exits and writes a screenshot' ($exit -eq 0 -and (Test-Path (Join-Path $Shots 'light.png'))) "exit $exit; screenshot $Shots\light.png"

    # 4: the Archived filter, started from the flag.
    $archivedDir = Join-Path $work 'archived'
    New-Item -ItemType Directory -Force $archivedDir | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $archivedDir 'library.json'), $seed.Replace("`r`n", "`n"), $utf8)
    $exit = Invoke-App (Join-Path $archivedDir 'state.json') @('--rail-filter', 'archived') (Join-Path $Shots 'archived.png')
    Add-Check 'archived: the Archived filter runs and writes a screenshot' ($exit -eq 0 -and (Test-Path (Join-Path $Shots 'archived.png'))) "exit $exit; screenshot $Shots\archived.png"

    # 5: D-L3, an unreadable library.json.
    $bad = Join-Path $work 'bad'
    New-Item -ItemType Directory -Force $bad | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $bad 'library.json'), $broken, $utf8)
    $before = (Get-FileHash (Join-Path $bad 'library.json')).Hash
    $exit = Invoke-App (Join-Path $bad 'state.json') @('--rail-filter', 'all') (Join-Path $Shots 'bad.png')
    $log = Join-Path $bad 'logs\app.log'
    $logged = (Test-Path $log) -and (Select-String -Path $log -Pattern 'library not read' -Quiet)
    Add-Check 'D-L3: an unreadable library.json is reported in the log, and the app still runs' ($exit -eq 0 -and $logged) "exit $exit; log line present: $logged"
    Add-Check 'D-L3: the unreadable library.json is not overwritten' ((Get-FileHash (Join-Path $bad 'library.json')).Hash -eq $before) 'the file was left as it was'
}
finally {
    Remove-Item Env:OPENTAIL_BULLSHARK_EXPAND -ErrorAction SilentlyContinue
    if (-not $Keep) { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
}

$results | ForEach-Object { '{0}  {1}' -f $_.Result, $_.Check; '      ' + $_.Detail }
if ($results | Where-Object { $_.Result -eq 'FAIL' }) { exit 1 } else { exit 0 }
