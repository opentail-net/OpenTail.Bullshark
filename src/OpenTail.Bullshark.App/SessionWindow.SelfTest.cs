using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Terminal;

namespace OpenTail.Bullshark.App;

/// <summary>
/// S3 checks, off unless a flag is given. <c>--selftest &lt;result.json&gt;</c> runs a counter in an embedded terminal:
/// it hides the terminal for about 10 seconds, shows it again, sends a key, and writes the result. It uses a counter,
/// not a Claude session, so it costs nothing and touches no owner session. <c>--shot &lt;png&gt;</c> saves the window
/// from the screen (CopyFromScreen, so hosted terminal content is included).
/// </summary>
public partial class SessionWindow
{
    private ExtendedTerminalControl? _selfTestTerminal;

    private void StartSelfTestOrScreenshot()
    {
        if (_options.SelfTestPath is not null) StartSelfTest(_options.SelfTestPath);
        if (_options.ShotPath is not null) ScheduleScreenshot(_options.ShotPath, _options.ShotAfterSeconds);
    }

    private void ScheduleScreenshot(string path, double afterSeconds)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(afterSeconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                SaveScreenshot(path);
            }
            catch (Exception ex)
            {
                AppLog.Write($"screenshot failed: {ex.GetType().Name}: {ex.Message}");
            }
            // A screenshot run closes after the picture, unless the self-test is still running.
            if (_options.SelfTestPath is null) Close();
        };
        timer.Start();
    }

    private void StartSelfTest(string resultPath)
    {
        var ticker = WriteTickerScript();
        _selfTestTerminal = new ExtendedTerminalControl
        {
            StartupCommandLine = $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{ticker}\"",
            WorkingDirectory = Path.GetDirectoryName(ticker),
            LogConPTYOutput = true,
            Visibility = Visibility.Hidden,
        };
        Pane.Children.Add(_selfTestTerminal);
        ShowOnly(_selfTestTerminal);

        var started = DateTimeOffset.UtcNow;
        int ticksAtHide = 0, ticksAtShow = 0;
        bool hidden = false, shown = false, keySent = false;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            var elapsed = (DateTimeOffset.UtcNow - started).TotalSeconds;

            if (!hidden && elapsed >= 4)
            {
                hidden = true;
                ticksAtHide = LastTick(_selfTestTerminal);
                // Show something else in the pane: the terminal is hidden, not collapsed, and keeps its size.
                ShowOnly(_setup);
            }

            if (hidden && !shown && elapsed >= 14)
            {
                shown = true;
                ticksAtShow = LastTick(_selfTestTerminal);
                ShowOnly(_selfTestTerminal);
            }

            if (shown && !keySent && elapsed >= 16)
            {
                keySent = true;
                _selfTestTerminal.ConPTYTerm?.WriteToTerm("q");
            }

            if (elapsed >= 19)
            {
                timer.Stop();
                var text = ConsoleText(_selfTestTerminal);
                var result = new
                {
                    started = _selfTestTerminal.ConPTYTerm?.TermProcIsStarted == true,
                    processAlive = _selfTestTerminal.ConPTYTerm?.Process is { HasExited: false },
                    ticksAtHide,
                    ticksAtShow,
                    ticksWhileHidden = ticksAtShow - ticksAtHide,
                    ticksAfterShow = LastTick(_selfTestTerminal) - ticksAtShow,
                    keyEcho = Regex.IsMatch(text, @"KEY\s+q"),
                };
                try
                {
                    File.WriteAllText(resultPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception ex)
                {
                    AppLog.Write($"self-test result not written: {ex.Message}");
                }
                Close();
            }
        };
        timer.Start();
    }

    private static string WriteTickerScript()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bullshark-selftest");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "ticker.ps1");
        File.WriteAllText(path, """
            $i = 0
            while ($true) {
                $i++
                Write-Output ('TICK ' + $i)
                $end = (Get-Date).AddSeconds(1)
                while ((Get-Date) -lt $end) {
                    while ([Console]::KeyAvailable) {
                        $k = [Console]::ReadKey($true)
                        Write-Output ('KEY ' + $k.KeyChar)
                    }
                    Start-Sleep -Milliseconds 50
                }
            }
            """);
        return path;
    }

    private static string ConsoleText(ExtendedTerminalControl? terminal)
    {
        try
        {
            return terminal?.ConPTYTerm?.GetConsoleText(true) ?? string.Empty;
        }
        catch (Exception ex)
        {
            AppLog.Write($"console text not read: {ex.GetType().Name}: {ex.Message}");
            return string.Empty;
        }
    }

    private static int LastTick(ExtendedTerminalControl? terminal)
    {
        var matches = Regex.Matches(ConsoleText(terminal), @"TICK (\d+)");
        return matches.Count == 0 ? 0 : matches.Max(m => int.Parse(m.Groups[1].Value));
    }

    /// <summary>
    /// Asks this window to draw itself into a bitmap (PrintWindow). It cannot pick up another program's pixels, so it
    /// needs no screen copy and no z-order. Hosted terminal content may come out blank; a blank terminal in the picture
    /// is not a failure by itself (the self-test checks the terminal).
    /// </summary>
    private void SaveScreenshot(string path)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        int w = (int)(ActualWidth * dpi.DpiScaleX);
        int h = (int)(ActualHeight * dpi.DpiScaleY);

        using var bmp = new System.Drawing.Bitmap(w, h);
        bool printed;
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            try
            {
                printed = PrintWindow(handle, hdc, PwRenderFullContent);
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }
        if (!printed)
        {
            AppLog.Write("screenshot not taken: PrintWindow failed");
            return;
        }
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        AppLog.Write($"screenshot saved: {w}x{h}");
    }

    private const uint PwRenderFullContent = 2;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
}
