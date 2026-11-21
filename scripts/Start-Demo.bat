@echo off
rem Starts OpenTail.Bullshark on the demo folder and task (the old launcher's arguments). For a normal start use Start-OpenTail.Bullshark.bat.
rem The first time, accept the trust prompt for the demo folder (use scripts\Open-Demo-Folder.bat).
setlocal
set "BULLSHARK_BIN=%~dp0..\src\OpenTail.Bullshark.App\bin"
set "BULLSHARK_EXE="
for /f "usebackq delims=" %%F in (`powershell -NoProfile -Command "Get-ChildItem -LiteralPath $env:BULLSHARK_BIN -Recurse -Filter OpenTail.Bullshark.App.exe -ErrorAction SilentlyContinue | Where-Object { $_.Directory.Name -in 'net10.0-windows10.0.19041.0','net11.0-windows10.0.19041.0' -and $_.Directory.Parent.Name -in 'Debug','Release' } | Sort-Object @{Expression={$_.Directory.Name -like 'net11*'};Descending=$true}, LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName"`) do set "BULLSHARK_EXE=%%F"
if not defined BULLSHARK_EXE (
  echo No built OpenTail.Bullshark.App.exe was found under src\OpenTail.Bullshark.App\bin\Debug or \Release.
  echo Build it first:  dotnet build src\OpenTail.Bullshark.App
  pause
  exit /b 1
)
start "" "%BULLSHARK_EXE%" --workdir "%~dp0..\samples\demo-handoff" --task "Create a file named hello.txt in this folder that contains exactly the line: hello from the coder"
endlocal
