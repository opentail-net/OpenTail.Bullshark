@echo off
rem Starts OpenTail.Bullshark with no demo arguments: the newest built Debug or Release app (firstrun batch B5).
rem It is the build of src\OpenTail.Bullshark.App for net10.0 or net11.0 (-windows10.0.19041.0); the newest build wins. The demo folder and task are in Start-Demo.bat.
setlocal
set "BULLSHARK_BIN=%~dp0..\src\OpenTail.Bullshark.App\bin"
set "BULLSHARK_EXE="
for /f "usebackq delims=" %%F in (`powershell -NoProfile -Command "Get-ChildItem -LiteralPath $env:BULLSHARK_BIN -Recurse -Filter OpenTail.Bullshark.App.exe -ErrorAction SilentlyContinue | Where-Object { $_.Directory.Name -in 'net10.0-windows10.0.19041.0','net11.0-windows10.0.19041.0' -and $_.Directory.Parent.Name -in 'Debug','Release' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName"`) do set "BULLSHARK_EXE=%%F"
if not defined BULLSHARK_EXE (
  echo No built OpenTail.Bullshark.App.exe was found under src\OpenTail.Bullshark.App\bin\Debug or \Release.
  echo Build it first:  dotnet build src\OpenTail.Bullshark.App
  pause
  exit /b 1
)
start "" "%BULLSHARK_EXE%"
endlocal
