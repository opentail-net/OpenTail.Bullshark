# Runs the OpenTail.Bullshark spike window in smoke mode and reports what the embedded terminal captured.
# The app is started hidden with no inherited console, which is how an embedded session must be launched
# (see docs/findings/FINDINGS.md, finding F3).
param(
    [Parameter(Mandatory)] [string] $Command,
    [double] $Seconds = 6,
    [string] $Name = 'run',
    [string] $Type,
    [double] $TypeAt = 4,
    [string] $OutDir = (Join-Path $env:TEMP 'bullshark-spike')
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repo 'src\OpenTail.Bullshark.App\bin\Debug\net10.0-windows\OpenTail.Bullshark.App.exe'
if (-not (Test-Path $exe)) { throw "Build the app first: $exe not found" }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$out = Join-Path $OutDir "$Name.txt"
$shot = Join-Path $OutDir "$Name.png"
Remove-Item -ErrorAction SilentlyContinue $out, "$out.diag.txt", $shot

# Quote the command so that spaces inside it survive the Windows command-line split.
$quotedCommand = '"' + $Command.Replace('"', '\"') + '"'
$argLine = "--smoke --log --seconds $Seconds --command $quotedCommand --out `"$out`" --shot `"$shot`""
if ($Type) { $argLine += " --type `"$($Type.Replace('"', '\"'))`" --type-at $TypeAt" }

$proc = Start-Process -FilePath $exe -ArgumentList $argLine -WindowStyle Hidden -PassThru -Wait
[pscustomobject]@{
    Name     = $Name
    ExitCode = $proc.ExitCode
    Output   = $out
    Shot     = $shot
    Diag     = (Get-Content "$out.diag.txt" -Raw -ErrorAction SilentlyContinue)
    Text     = (Get-Content $out -Raw -ErrorAction SilentlyContinue)
}
