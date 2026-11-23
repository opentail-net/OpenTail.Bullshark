<#
.SYNOPSIS
Runs the live tests: the ones that start the real Claude and Codex programs and can spend tokens.

.DESCRIPTION
CI never runs these (they carry Trait Category=Live and CI filters them out). Run this on your PC when you want that check.
Only the classes you pick run, and their gates are set for this run only. Nothing is saved to the environment.

  -Codex       CodexModelListLiveTests: starts `codex app-server` (gate OPENTAIL_CODEX_LIVE=1).
  -Supervisor  SupervisorLiveTests: starts `claude` (gate OPENTAIL_BULLSHARK_LIVE=1).
  -Reviewer    ReviewerLiveTests: starts `claude` and spends tokens (gates OPENTAIL_BULLSHARK_LIVE=1 and OPENTAIL_BULLSHARK_LIVE_REVIEW=1).

With no switch, nothing runs.

.EXAMPLE
.\scripts\Run-LiveTests.ps1 -Codex
#>
param(
    [switch]$Codex,
    [switch]$Supervisor,
    [switch]$Reviewer
)

$ErrorActionPreference = 'Stop'

$classes = @()
if ($Codex) { $classes += 'CodexModelListLiveTests' }
if ($Supervisor) { $classes += 'SupervisorLiveTests' }
if ($Reviewer) { $classes += 'ReviewerLiveTests' }

if ($classes.Count -eq 0) {
    Write-Host 'Nothing selected. Use -Codex, -Supervisor or -Reviewer (see the top of this script).'
    exit 0
}

function Set-Gate([string]$Name, [bool]$On) {
    if ($On) { Set-Item -Path "Env:$Name" -Value '1' }
    else { Remove-Item -Path "Env:$Name" -ErrorAction SilentlyContinue }
}

Set-Gate 'OPENTAIL_CODEX_LIVE' ([bool]$Codex)
Set-Gate 'OPENTAIL_BULLSHARK_LIVE' ([bool]($Supervisor -or $Reviewer))
Set-Gate 'OPENTAIL_BULLSHARK_LIVE_REVIEW' ([bool]$Reviewer)

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tests\OpenTail.Bullshark.Core.Tests\OpenTail.Bullshark.Core.Tests.csproj'
$filter = 'Category=Live&(' + (($classes | ForEach-Object { "FullyQualifiedName~$_" }) -join '|') + ')'

Write-Host "Live tests: $($classes -join ', '). These start the real programs and can spend tokens."
dotnet test $project -nodeReuse:false --filter $filter
exit $LASTEXITCODE
