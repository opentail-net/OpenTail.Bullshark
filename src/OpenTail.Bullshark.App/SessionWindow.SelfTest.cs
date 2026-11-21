using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using OpenTail.Bullshark.Core.Health;
using OpenTail.Bullshark.Core.Ui;
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
        if (_options.SelfTestPath is not null)
        {
            if (_options.SelfTestMode == "move") StartMoveTest(_options.SelfTestPath);
            else if (_options.SelfTestMode == "split") StartSplitTest(_options.SelfTestPath);
            else StartSelfTest(_options.SelfTestPath);
        }
        if (_options.ShotPath is not null) ScheduleScreenshot(_options.ShotPath, _options.ShotAfterSeconds);
    }

    /// <summary>
    /// Opens the screen that <c>--open-tab</c> names, with no click (round 4, item 5), for the screenshots. A changes request names a
    /// session by its run name. It does nothing when no record has that name, so the window opens as it would without the option.
    /// The Insights and Settings screens (2026-11-18) open their sub-tab; storage and timeline open Insights on theirs, as before.
    /// </summary>
    private void OpenTestScreen()
    {
        var request = _options.OpenTab;
        if (request is null) return;
        switch (request.Screen)
        {
            case TestScreen.Storage:
                OpenInsights(InsightsTab.Storage);
                break;
            case TestScreen.Timeline:
                OpenInsights(InsightsTab.Timeline);
                break;
            case TestScreen.Insights:
            case TestScreen.Away:
                OpenInsights(InsightsTab.Away);
                break;
            case TestScreen.Night:
                OpenInsights(InsightsTab.Night);
                break;
            case TestScreen.Usage:
                OpenInsights(InsightsTab.Usage);
                break;
            case TestScreen.Settings:
                OpenSettings(SettingsTab.General);
                break;
            case TestScreen.SettingsNaming:
                OpenSettings(SettingsTab.Naming);
                break;
            case TestScreen.SettingsClaude:
                OpenSettings(SettingsTab.Claude);
                break;
            case TestScreen.Setup:
                OpenSetupTab(null);
                break;
            case TestScreen.Shortcuts:
                // The sheet is shown, not modal, so the screenshot can draw it; the picture is of the sheet (firstrun batch B7).
                _shotDialog = new ShortcutsDialog(Palette) { Owner = this };
                _shotDialog.Show();
                break;
            case TestScreen.SetupCheck:
                _shotDialog = new SetupCheckDialog(Palette, () => SetupCheck.Run(SetupCheck.DefaultClaudeDir, SetupCheck.DefaultClaudeJsonPath))
                {
                    Owner = this,
                };
                _shotDialog.Show();
                break;
            case TestScreen.Changes:
                var record = ReadRecords().Values.FirstOrDefault(r => string.Equals(r.Name, request.SessionName, StringComparison.Ordinal));
                if (record is not null && record.SessionId.Length > 0) OpenChanges(record.SessionId, record.Name);
                break;
        }
    }

    /// <summary>The dialog a screen flag opened, which the picture is of (null for the window itself).</summary>
    private System.Windows.Window? _shotDialog;

    private void ScheduleScreenshot(string path, double afterSeconds)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(afterSeconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                SaveScreenshot(path, _shotDialog);
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
            WorkingDirectory = Path.GetDirectoryName(ticker)!, // the ticker is written under the temp folder, so its folder is a full path
            LogConPTYOutput = true,
            Visibility = Visibility.Hidden,
        };
        Pane.Children.Add(_selfTestTerminal);
        ConfigureTerminal(_selfTestTerminal);
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

    /// <summary>
    /// The spike of docs/plans/window.md (T2): <c>--selftest &lt;result.json&gt; --selftest-mode move</c> moves a running
    /// terminal from the pane to a second grid in the same window, and back. After each move it sends a key. The result says
    /// whether the process lived, whether the counter went on, whether each key came back, and the terminal's width in each place.
    /// </summary>
    private void StartMoveTest(string resultPath)
    {
        var ticker = WriteTickerScript();
        _selfTestTerminal = new ExtendedTerminalControl
        {
            StartupCommandLine = $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{ticker}\"",
            WorkingDirectory = Path.GetDirectoryName(ticker)!,
            LogConPTYOutput = true,
            Visibility = Visibility.Hidden,
        };
        Pane.Children.Add(_selfTestTerminal);
        ShowOnly(_selfTestTerminal);

        // The second parent: a grid over the right half of the pane. It is a different parent with a different size.
        var other = new System.Windows.Controls.Grid { HorizontalAlignment = HorizontalAlignment.Right };
        other.SetBinding(WidthProperty, new System.Windows.Data.Binding("ActualWidth")
        {
            Source = Pane,
            Converter = HalfWidth.Instance,
        });
        Pane.Children.Add(other);

        var started = DateTimeOffset.UtcNow;
        int step = 0, ticksBeforeMove = 0, ticksInOther = 0, ticksBack = 0;
        double widthFirst = 0, widthOther = 0, widthBack = 0;
        string? moveError = null;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            var elapsed = (DateTimeOffset.UtcNow - started).TotalSeconds;
            try
            {
                if (step == 0 && elapsed >= 4)
                {
                    step = 1;
                    ticksBeforeMove = LastTick(_selfTestTerminal);
                    widthFirst = _selfTestTerminal.ActualWidth;
                    Pane.Children.Remove(_selfTestTerminal);
                    other.Children.Add(_selfTestTerminal);
                }
                if (step == 1 && elapsed >= 8)
                {
                    step = 2;
                    widthOther = _selfTestTerminal.ActualWidth;
                    _selfTestTerminal.ConPTYTerm?.WriteToTerm("a");
                }
                if (step == 2 && elapsed >= 10)
                {
                    step = 3;
                    ticksInOther = LastTick(_selfTestTerminal);
                    other.Children.Remove(_selfTestTerminal);
                    Pane.Children.Add(_selfTestTerminal);
                }
                if (step == 3 && elapsed >= 13)
                {
                    step = 4;
                    widthBack = _selfTestTerminal.ActualWidth;
                    _selfTestTerminal.ConPTYTerm?.WriteToTerm("b");
                }
            }
            catch (Exception ex)
            {
                moveError ??= $"step {step}: {ex.GetType().Name}: {ex.Message}";
                AppLog.Write($"move test failed at {moveError}");
            }

            if (elapsed >= 16)
            {
                timer.Stop();
                ticksBack = LastTick(_selfTestTerminal);
                var text = ConsoleText(_selfTestTerminal);
                var result = new
                {
                    started = _selfTestTerminal.ConPTYTerm?.TermProcIsStarted == true,
                    processAlive = _selfTestTerminal.ConPTYTerm?.Process is { HasExited: false },
                    ticksBeforeMove,
                    ticksInOther,
                    ticksBack,
                    keyInOther = Regex.IsMatch(text, @"KEY\s+a"),
                    keyBack = Regex.IsMatch(text, @"KEY\s+b"),
                    widthFirst,
                    widthOther,
                    widthBack,
                    moveError,
                };
                try
                {
                    File.WriteAllText(resultPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception ex)
                {
                    AppLog.Write($"move test result not written: {ex.Message}");
                }
                Close();
            }
        };
        timer.Start();
    }

    /// <summary>Half of a width, for the move test's second grid.</summary>
    private sealed class HalfWidth : System.Windows.Data.IValueConverter
    {
        public static readonly HalfWidth Instance = new();

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is double width ? width / 2 : 0d;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// In the split self-test every session's terminal runs the counter, not <c>claude attach</c>, so the tabs and the panes are
    /// tested with live terminals and no Claude session. Null in every other run.
    /// </summary>
    private string? SelfTestCommand => _options.SelfTestMode == "split"
        ? $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{WriteTickerScript()}\""
        : null;

    /// <summary>
    /// The check of the split (docs/plans/window.md, T2): <c>--selftest &lt;result.json&gt; --selftest-mode split</c>, with a
    /// state file that holds two records. It moves the second session's tab to the right pane, sends a key to each terminal,
    /// checks which terminal a paste would go to after the script clicks in each, closes the right tab, and writes what it saw.
    /// </summary>
    private void StartSplitTest(string resultPath)
    {
        var started = DateTimeOffset.UtcNow;
        var step = 0;
        var seen = new Dictionary<string, object?>();
        OpenTab? leftTab = null, rightTab = null;
        ExtendedTerminalControl? leftTerm = null, rightTerm = null;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            var elapsed = (DateTimeOffset.UtcNow - started).TotalSeconds;
            try
            {
                if (step == 0 && elapsed >= 5)
                {
                    step = 1;
                    var sessions = _tabs.Where(t => t.Kind == TabKind.Session).ToList();
                    seen["sessionTabs"] = sessions.Count;
                    seen["tabOrder"] = string.Join(",", sessions.Select(t => t.Title));
                    seen["rightAtStart"] = string.Join(",", sessions.Where(IsRight).Select(t => t.Title));
                    if (sessions.Count >= 2)
                    {
                        rightTab = sessions.FirstOrDefault(IsRight) ?? sessions[1];
                        leftTab = sessions.First(t => !ReferenceEquals(t, rightTab));
                        ActivateTab(leftTab);
                        MoveToRight(rightTab);
                        _terminals.TryGetValue(leftTab.Key, out leftTerm);
                        _terminals.TryGetValue(rightTab.Key, out rightTerm);
                    }
                }
                if (step == 1 && elapsed >= 8 && leftTerm is not null && rightTerm is not null)
                {
                    step = 2;
                    seen["splitOpen"] = SplitOpen;
                    seen["rightParentIsRightPane"] = ReferenceEquals(rightTerm.Parent, PaneB);
                    seen["leftParentIsLeftPane"] = ReferenceEquals(leftTerm.Parent, Pane);
                    seen["bothVisible"] = leftTerm.IsVisible && rightTerm.IsVisible;
                    seen["leftWidth"] = Math.Round(leftTerm.ActualWidth);
                    seen["rightWidth"] = Math.Round(rightTerm.ActualWidth);
                    seen["leftStripTabs"] = TabStrip.Children.OfType<System.Windows.Controls.Border>().Count(b => b.Tag is OpenTab);
                    seen["rightStripTabs"] = TabStripB.Children.OfType<System.Windows.Controls.Border>().Count(b => b.Tag is OpenTab);
                    seen["savedLayout"] = File.Exists(TabOrderPath) ? File.ReadAllText(TabOrderPath) : null;
                    leftTerm.ConPTYTerm?.WriteToTerm("l");
                    rightTerm.ConPTYTerm?.WriteToTerm("r");
                }
                if (step == 2 && elapsed >= 11)
                {
                    step = 3;
                    // The script clicks in the left terminal between 9 and 10 seconds. A paste must follow that click.
                    seen["pasteGoesLeftAfterAClickThere"] = ReferenceEquals(PasteTarget(), leftTerm);
                }
                if (step == 3 && elapsed >= 13)
                {
                    step = 5;
                    // The script clicks in the right terminal between 11.5 and 12.5 seconds.
                    seen["pasteGoesRightAfterAClickThere"] = ReferenceEquals(PasteTarget(), rightTerm);
                    seen["leftKeyOnlyInLeft"] = Regex.IsMatch(ConsoleText(leftTerm), @"KEY\s+l") && !Regex.IsMatch(ConsoleText(leftTerm), @"KEY\s+r");
                    seen["rightKeyOnlyInRight"] = Regex.IsMatch(ConsoleText(rightTerm), @"KEY\s+r") && !Regex.IsMatch(ConsoleText(rightTerm), @"KEY\s+l");
                    seen["ticksLeft"] = LastTick(leftTerm);
                    seen["ticksRight"] = LastTick(rightTerm);
                }
                if (step == 5 && elapsed >= 15)
                {
                    step = 6;
                    // The 15 s rail refresh has run by now with the split open; the layout must have survived it.
                    seen["splitOpenAfterRefresh"] = SplitOpen && ReferenceEquals(rightTerm!.Parent, PaneB);
                    CloseTab(rightTab!);
                }
                if (step == 6 && elapsed >= 17)
                {
                    step = 7;
                    seen["splitClosedAfterTabClosed"] = !SplitOpen;
                    seen["closedTerminalBackAtLeft"] = ReferenceEquals(rightTerm!.Parent, Pane) && !rightTerm.IsVisible;
                    seen["closedTerminalAlive"] = rightTerm.ConPTYTerm?.Process is { HasExited: false };
                    seen["ticksRightAfterClose"] = LastTick(rightTerm);
                    seen["leftAlive"] = leftTerm!.ConPTYTerm?.Process is { HasExited: false };
                }
            }
            catch (Exception ex)
            {
                seen["error"] = $"step {step}: {ex.GetType().Name}: {ex.Message}";
                AppLog.Write($"split test failed at step {step}: {ex}");
                step = 99;
            }

            if (elapsed >= 18 || step == 99)
            {
                timer.Stop();
                try
                {
                    File.WriteAllText(resultPath, JsonSerializer.Serialize(seen, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception ex)
                {
                    AppLog.Write($"split test result not written: {ex.Message}");
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
    private void SaveScreenshot(string path, System.Windows.Window? target = null)
    {
        // A dialog the screen flag opened (the shortcut sheet, the setup check) is drawn by itself; otherwise this window is (firstrun batch B7, B8).
        var shown = target ?? this;
        var handle = new System.Windows.Interop.WindowInteropHelper(shown).Handle;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(shown);
        int w = (int)(shown.ActualWidth * dpi.DpiScaleX);
        int h = (int)(shown.ActualHeight * dpi.DpiScaleY);

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
