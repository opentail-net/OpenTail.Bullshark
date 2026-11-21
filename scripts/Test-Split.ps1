# T2 acceptance (docs/plans/window.md): the split pane, with two live terminals.
# The check uses a PowerShell counter in place of each session, so it costs nothing and touches no owner session.
# It runs the window twice with --no-pass and scratch paths. Each run closes itself after about 20 seconds.
# Run 1: a tab is moved to the right pane, keys and the paste target are checked, and the right tab is closed.
# Run 2: tabs.json says a session was at the right, and the window must open with it there.
# The script clicks twice inside the window (once in each terminal), so leave the mouse alone while it runs.
#
#   pwsh scripts\Test-Split.ps1 [-Exe <path to OpenTail.Bullshark.App.exe>] [-Shot <png>]
#
# Each check prints PASS or FAIL. The script exits with 1 if any check fails.
param(
    [string]$Exe = ((@(Get-ChildItem -Path (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\OpenTail.Bullshark.App\bin\Debug\net1*-windows10.0.19041.0\OpenTail.Bullshark.App.exe') -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -ExpandProperty FullName) + @(Join-Path (Split-Path -Parent $PSScriptRoot) 'src\OpenTail.Bullshark.App\bin\Debug\net11.0-windows10.0.19041.0\OpenTail.Bullshark.App.exe'))[0]),
    [string]$Shot
)
$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SplitTestNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

    // Clicks only when the window at the point belongs to the given top-level window. Returns whether it clicked.
    public static bool ClickIn(IntPtr top, int x, int y) {
        var at = WindowFromPoint(new POINT { X = x, Y = y });
        if (at == IntPtr.Zero || GetAncestor(at, 2) != top) return false;
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(100);
        at = WindowFromPoint(new POINT { X = x, Y = y });
        if (at == IntPtr.Zero || GetAncestor(at, 2) != top) return false;
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
        return true;
    }
}
'@

[SplitTestNative]::SetProcessDPIAware() | Out-Null
$clickedLeft = $false
$clickedRight = $false
$work = Join-Path $env:TEMP ('bullshark-split-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$folder = Join-Path $work 'folder'
New-Item -ItemType Directory -Force $folder | Out-Null
$state = Join-Path $work 'state.json'
$idA = '11111111-aaaa-4aaa-8aaa-111111111111'
$idB = '22222222-bbbb-4bbb-8bbb-222222222222'

function Write-State {
    $now = (Get-Date).ToUniversalTime().ToString('o')
    @(
        @{ Role = 'Planner'; SessionId = $idA; Name = 'test-a'; StartedAt = $now; Restarts = 0; RestartTimes = @(); LastState = $null; LastAlert = $null },
        @{ Role = 'Coder'; SessionId = $idB; Name = 'test-b'; StartedAt = $now; Restarts = 0; RestartTimes = @(); LastState = $null; LastAlert = $null }
    ) | ConvertTo-Json -Depth 4 | Set-Content -Path $state -Encoding utf8
}

function Invoke-Run([string]$result, [bool]$click, [string]$shot) {
    $list = @('--selftest', $result, '--selftest-mode', 'split', '--no-pass', '--state', $state, '--workdir', $folder)
    if ($shot) { $list += @('--shot', $shot, '--shot-after', '9') }
    $p = Start-Process -FilePath $Exe -ArgumentList $list -PassThru
    if ($click) {
        # At 9.5 seconds the split is open. One click in the left terminal: a paste must then go to the left.
        Start-Sleep -Milliseconds 9500
        $p.Refresh()
        $rect = New-Object SplitTestNative+RECT
        if ($p.MainWindowHandle -ne [IntPtr]::Zero -and [SplitTestNative]::GetWindowRect($p.MainWindowHandle, [ref]$rect)) {
            # A click is made only when the window under the point is the test window. If another window covers it, no click
            # is made, and the two click checks are reported as not run.
            $y = $rect.Top + [int](($rect.Bottom - $rect.Top) * 0.5)
            $script:clickedLeft = [SplitTestNative]::ClickIn($p.MainWindowHandle, $rect.Left + [int](($rect.Right - $rect.Left) * 0.42), $y)

            # At 11.8 seconds, one click in the right terminal: a paste must then go to the right.
            Start-Sleep -Milliseconds 2200
            $script:clickedRight = [SplitTestNative]::ClickIn($p.MainWindowHandle, $rect.Left + [int](($rect.Right - $rect.Left) * 0.83), $y)
        }
    }
    if (-not $p.WaitForExit(90000)) {
        $p.Kill()
        throw 'the self-test did not finish within 90 seconds'
    }
    if (-not (Test-Path $result)) { throw 'the self-test wrote no result file' }
    Get-Content $result -Raw | ConvertFrom-Json
}

try {
    Write-State
    $r1 = Invoke-Run (Join-Path $work 'result1.json') $true $Shot

    # Run 2 starts from a saved layout: test-b first, and at the right.
    @{ Order = @($idB, $idA); Right = @($idB) } | ConvertTo-Json | Set-Content -Path (Join-Path $work 'tabs.json') -Encoding utf8
    $r2 = Invoke-Run (Join-Path $work 'result2.json') $false $null

    $checks = @(
        [pscustomobject]@{ Test = 'no error in run 1';                                            Result = (-not $r1.error) },
        [pscustomobject]@{ Test = 'two session tabs, none at the right at first';                 Result = ($r1.sessionTabs -eq 2 -and -not $r1.rightAtStart) },
        [pscustomobject]@{ Test = 'the split opens and the terminal is in the right pane';        Result = ([bool]$r1.splitOpen -and [bool]$r1.rightParentIsRightPane -and [bool]$r1.leftParentIsLeftPane) },
        [pscustomobject]@{ Test = 'both terminals are visible, each with a width';                Result = ([bool]$r1.bothVisible -and $r1.leftWidth -gt 100 -and $r1.rightWidth -gt 100) },
        [pscustomobject]@{ Test = 'each strip has one session tab';                               Result = ($r1.leftStripTabs -ge 1 -and $r1.rightStripTabs -eq 1) },
        [pscustomobject]@{ Test = 'the layout is saved with the session at the right';            Result = ($r1.savedLayout -match $idB) },
        [pscustomobject]@{ Test = 'a key sent to each terminal reaches only that terminal';       Result = ([bool]$r1.leftKeyOnlyInLeft -and [bool]$r1.rightKeyOnlyInRight) },
        [pscustomobject]@{ Test = 'a paste follows a mouse click in the left terminal';           Result = $(if ($clickedLeft) { [bool]$r1.pasteGoesLeftAfterAClickThere } else { $null }) },
        [pscustomobject]@{ Test = 'a paste follows a mouse click in the right terminal';          Result = $(if ($clickedRight) { [bool]$r1.pasteGoesRightAfterAClickThere } else { $null }) },
        [pscustomobject]@{ Test = 'both counters keep running (at least 5 ticks each)';           Result = ($r1.ticksLeft -ge 5 -and $r1.ticksRight -ge 5) },
        [pscustomobject]@{ Test = 'the split survives a rail refresh';                            Result = [bool]$r1.splitOpenAfterRefresh },
        [pscustomobject]@{ Test = 'closing the right tab closes the split';                       Result = [bool]$r1.splitClosedAfterTabClosed },
        [pscustomobject]@{ Test = 'the closed tab''s terminal is back at the left, hidden, alive'; Result = ([bool]$r1.closedTerminalBackAtLeft -and [bool]$r1.closedTerminalAlive -and $r1.ticksRightAfterClose -gt $r1.ticksRight) },
        [pscustomobject]@{ Test = 'no error in run 2';                                            Result = (-not $r2.error) },
        [pscustomobject]@{ Test = 'run 2 opens in the saved order, with the session at the right'; Result = ($r2.tabOrder -eq 'test-b,test-a' -and $r2.rightAtStart -eq 'test-b') }
    )
    $checks | ForEach-Object { '{0}  {1}' -f $(if ($null -eq $_.Result) { 'SKIP' } elseif ($_.Result) { 'PASS' } else { 'FAIL' }), $_.Test }
    if ($checks | Where-Object { $null -eq $_.Result }) { 'SKIP means the test window was covered by another window, so no click was made.' }
    if ($checks | Where-Object { $null -ne $_.Result -and -not $_.Result }) {
        'run 1: ' + ($r1 | ConvertTo-Json -Compress)
        'run 2: ' + ($r2 | ConvertTo-Json -Compress)
        Get-ChildItem $work -Recurse -Filter app.log | ForEach-Object { Get-Content $_.FullName -Tail 15 }
        exit 1
    }
    exit 0
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
