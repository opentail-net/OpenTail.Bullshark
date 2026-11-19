using System.IO;
using System.Runtime.ExceptionServices;
using WpfDispatcher = System.Windows.Threading.Dispatcher;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// One WPF thread for every test in this project (perf and testability batch, Q7). WPF controls belong to a single-threaded apartment with a
/// dispatcher, and the test runner's threads have neither, so the tests run their work on this thread and wait for its results. No application
/// object is made here: the app's own start (App.OnStartup) is never reached, and it refuses to open its window under the test flag anyway. The
/// styles a view needs are loaded by the test that needs them (see PillTabsTests).
/// </summary>
internal static class UiThread
{
    private static readonly object Gate = new();
    private static WpfDispatcher? _dispatcher;

    /// <summary>The dispatcher of the WPF thread, started on first use.</summary>
    private static WpfDispatcher WpfThread()
    {
        lock (Gate)
        {
            if (_dispatcher is not null) return _dispatcher;
            // The flag first, before anything else: the app's start refuses to open its window while it is set.
            Environment.SetEnvironmentVariable(TestGuard.Flag, "1");
            // The log goes to a folder of this run's own, never to the owner's log (AppLog defaults to the owner's folder).
            AppLog.FollowStateFile(Path.Combine(Path.GetTempPath(), "bullshark-app-tests-" + Guid.NewGuid().ToString("N"), "state.json"));
            var ready = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                _dispatcher = WpfDispatcher.CurrentDispatcher;
                ready.Set();
                WpfDispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "bullshark-wpf-tests",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();
            return _dispatcher!;
        }
    }

    /// <summary>Runs the work on the WPF thread and returns its result. An exception from the work is thrown again here.</summary>
    public static T Run<T>(Func<T> work)
    {
        try
        {
            return WpfThread().Invoke(work);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ExceptionDispatchInfo.Capture(ex).Throw();
            throw;
        }
    }

    /// <summary>Runs the work on the WPF thread and waits for it.</summary>
    public static void Run(Action work) => Run<bool>(() => { work(); return true; });

    /// <summary>
    /// Waits, on the test's own thread, until a condition holds on the WPF thread: the views finish their reads on pool threads and draw on the WPF
    /// thread afterwards. False when the condition did not hold within the time.
    /// </summary>
    public static bool WaitUntil(Func<bool> condition, int timeoutMilliseconds = 10_000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMilliseconds)
        {
            if (Run(condition)) return true;
            Thread.Sleep(20);
        }
        return Run(condition);
    }
}
