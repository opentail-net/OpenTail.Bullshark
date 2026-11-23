<#
.SYNOPSIS
Runs what CI runs, on both targets, and lists what a push would send. Run it before `git push`.

.DESCRIPTION
Shows the commits that are not on origin/main yet, so you can read them before they go out (other sessions may share this repository).
Then it builds the solution and runs the tests that CI runs (everything except Category=Live), on net10.0 and net11.0.
It exits with 1 when the build or a test fails, so a failing run is not pushed by mistake.
After a push, check CI with: gh run list --branch main --limit 1
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

git fetch --quiet origin

Write-Host 'Commits a push would send:'
$pending = git log --oneline 'origin/main..main'
if (-not $pending) {
    Write-Host '  none: main is not ahead of origin/main.'
    exit 0
}
$pending | ForEach-Object { "  $_" }

$dirty = git status --porcelain
if ($dirty) {
    Write-Host 'Uncommitted changes (a push does not send them, but check them):'
    $dirty | ForEach-Object { "  $_" }
}

Write-Host 'Building the solution...'
dotnet build OpenTail.Bullshark.slnx -nodeReuse:false
if ($LASTEXITCODE -ne 0) {
    Write-Host 'BUILD FAILED: do not push.'
    exit 1
}

Write-Host 'Running the CI tests (not Category=Live)...'
dotnet test OpenTail.Bullshark.slnx -nodeReuse:false --no-build --filter 'Category!=Live'
if ($LASTEXITCODE -ne 0) {
    Write-Host 'TESTS FAILED: do not push.'
    exit 1
}

Write-Host 'Tests passed. Push, then check CI: gh run list --branch main --limit 1'
exit 0
