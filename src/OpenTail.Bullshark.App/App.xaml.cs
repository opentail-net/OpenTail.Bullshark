using System.Windows;

namespace OpenTail.Bullshark.App;

/// <summary>Application entry point. Builds the window from command-line options.</summary>
public partial class App : Application
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);

    protected override void OnStartup(StartupEventArgs e)
    {
        // A run with --state keeps its log beside its state file, so it never writes into the default folder.
        var stateAt = Array.IndexOf(e.Args, "--state");
        if (stateAt >= 0 && stateAt + 1 < e.Args.Length) AppLog.FollowStateFile(e.Args[stateAt + 1]);

        // Sessions must not write into the console or the redirected streams we were started with: their output
        // belongs to the embedded terminal (finding F3). Detach from the console and clear the standard handles.
        FreeConsole();
        SetStdHandle(-10, IntPtr.Zero); // STD_INPUT_HANDLE
        SetStdHandle(-11, IntPtr.Zero); // STD_OUTPUT_HANDLE
        SetStdHandle(-12, IntPtr.Zero); // STD_ERROR_HANDLE
        // WPF's hardware drawing paints blank on this machine's GPU, so its own controls (tabs, status bar) never show.
        // Software rendering fixes that, and the embedded terminals still draw (finding F38).
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        base.OnStartup(e);
        // With the standard handles cleared, nothing reaches a console, so unhandled errors go to the log. Each one also gets a redacted crash
        // file (firstrun batch B3), and the window says where it is on its status line. The UI error keeps going: Handled stays true.
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Write($"unhandled: {args.Exception}");
            CrashFiles.Record(args.Exception, "ui");
            CrashFiles.ShowOnWindow();
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            AppLog.Write($"unhandled (not the UI thread, the app ends): {args.ExceptionObject}");
            CrashFiles.Record(args.ExceptionObject as Exception, "domain");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Write($"unobserved task: {args.Exception}");
            CrashFiles.Record(args.Exception, "task");
            args.SetObserved();
        };
        // --smoke runs the P1 spike harness (used by scripts/Test-Spike.ps1). Otherwise open the OpenTail.Bullshark window.
        Window window;
        try
        {
            window = e.Args.Contains("--smoke")
                ? new MainWindow(SpikeOptions.Parse(e.Args))
                : new SessionWindow(LaunchOptions.Parse(e.Args));
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, "OpenTail.Bullshark", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
            return;
        }
        catch (Exception ex)
        {
            // Any other failure while the window is built must not end the start silently. The log has the details; the owner sees the message.
            AppLog.Write($"startup failed: {ex}");
            MessageBox.Show($"OpenTail.Bullshark could not open its window: {ex.Message}\n\nThe details are in the log.",
                "OpenTail.Bullshark", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        MainWindow = window;
        window.Show();
        // --crash-test (for the crash-file check only, never in a normal run): throws one exception on the UI thread, as a bug would, so the
        // crash path runs and the window shows its notice. The handler above keeps the app running.
        if (e.Args.Contains("--crash-test"))
        {
            Dispatcher.BeginInvoke(new Action(() =>
                throw new InvalidOperationException("crash-test: a deliberate exception from --crash-test")),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }
}

