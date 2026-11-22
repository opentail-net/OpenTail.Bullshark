using System.IO;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Terminal;

namespace OpenTail.Bullshark.App;

/// <summary>
/// P1 spike window: one embedded WPF-UI.Terminal running a command.
/// With --smoke, it captures the terminal text and a screenshot and exits,
/// so the result can be checked without a person watching.
/// </summary>
public partial class MainWindow : Window
{
    private readonly SpikeOptions _options;

    public MainWindow(SpikeOptions options)
    {
        _options = options;
        InitializeComponent();
        // Set before the window loads: the control may start its process as soon as it is created.
        Term.StartupCommandLine = _options.Command;
        Term.WorkingDirectory = _options.WorkingDirectory;
        Term.LogConPTYOutput = _options.LogOutput;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    /// <summary>
    /// Closing a tab detaches the viewer; the background session keeps running (see the README).
    /// Detach the terminal first so the control does not dispose it while the library is still using it.
    /// </summary>
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            var term = Term.DisconnectConPTYTerm();
            term?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Write($"detach failed: {ex.GetType().Name}");
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_options.Smoke) return;

        var started = DateTime.UtcNow;
        bool typed = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            var elapsed = (DateTime.UtcNow - started).TotalSeconds;
            if (!typed && _options.TypeText is not null && elapsed >= _options.TypeAt)
            {
                typed = true;
                // Typed input, as a person would send it: the same path the nudge would use.
                Term.ConPTYTerm?.WriteToTerm(_options.TypeText + "\r");
            }
            if (elapsed >= _options.Seconds)
            {
                timer.Stop();
                Capture();
                Application.Current.Shutdown();
            }
        };
        timer.Start();
    }

    private void Capture()
    {
        var term = Term.ConPTYTerm;
        var diag = new System.Text.StringBuilder();
        diag.AppendLine($"command={_options.Command}");
        diag.AppendLine($"termIsNull={term is null}");
        if (term is not null)
        {
            diag.AppendLine($"started={term.TermProcIsStarted}");
            diag.AppendLine($"processNull={term.Process is null}");
        }
        var text = term is null ? "<no ConPTY term>" : term.GetConsoleText(true);
        if (!string.IsNullOrEmpty(_options.OutputPath))
        {
            File.WriteAllText(_options.OutputPath, text);
            File.WriteAllText(_options.OutputPath + ".diag.txt", diag.ToString());
        }

        if (!string.IsNullOrEmpty(_options.ScreenshotPath))
            SaveScreenshot(_options.ScreenshotPath);
    }

    /// <summary>Captures the window's pixels from the screen (the terminal is a DirectX surface).</summary>
    private void SaveScreenshot(string path)
    {
        var origin = PointToScreen(new System.Windows.Point(0, 0));
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        int w = (int)(ActualWidth * dpi.DpiScaleX);
        int h = (int)(ActualHeight * dpi.DpiScaleY);
        using var bmp = new System.Drawing.Bitmap(w, h);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.CopyFromScreen((int)origin.X, (int)origin.Y, 0, 0, new System.Drawing.Size(w, h));
        }
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}

/// <summary>Command-line options for the spike.</summary>
public sealed record SpikeOptions(
    string Command,
    string? WorkingDirectory,
    bool Smoke,
    double Seconds,
    string? OutputPath,
    string? ScreenshotPath,
    bool LogOutput,
    string? TypeText,
    double TypeAt)
{
    public static SpikeOptions Parse(string[] args)
    {
        string command = "cmd.exe /k echo BULLSHARK_SPIKE_DEFAULT";
        string? cwd = null, output = null, shot = null, typeText = null;
        bool smoke = false, log = false;
        double seconds = 6, typeAt = 4;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--command": command = args[++i]; break;
                case "--cwd": cwd = args[++i]; break;
                case "--smoke": smoke = true; break;
                case "--log": log = true; break;
                case "--seconds": seconds = double.Parse(args[++i], inv); break;
                case "--out": output = args[++i]; break;
                case "--shot": shot = args[++i]; break;
                case "--type": typeText = args[++i]; break;
                case "--type-at": typeAt = double.Parse(args[++i], inv); break;
            }
        }
        return new SpikeOptions(command, cwd, smoke, seconds, output, shot, log, typeText, typeAt);
    }
}
