@echo off
rem Starts OpenTail.Bullshark: one window, one tab per session (the planner and the coder).
rem The first time, accept the trust prompt for the demo folder (use scripts\Open-Demo-Folder.bat).
start "" "%~dp0..\src\OpenTail.Bullshark.App\bin\Debug\net10.0-windows\OpenTail.Bullshark.App.exe" --workdir "%~dp0..\samples\demo-handoff" --task "Create a file named hello.txt in this folder that contains exactly the line: hello from the coder"
