using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The safety of the headless tests (Q7). The app refuses to open its window in a test host (App.OnStartup checks
/// OPENTAIL_BULLSHARK_UNDER_TEST), and this module sets that flag before any test code runs. The count of claude.exe processes is taken here too, so
/// the whole run can be checked against it. The count only reads the process list; nothing is stopped.
/// </summary>
internal static class TestGuard
{
    /// <summary>The name of the environment flag the app checks (the same name as in the app's RunningUnderTest).</summary>
    public const string Flag = "OPENTAIL_BULLSHARK_UNDER_TEST";

    /// <summary>The number of claude.exe processes when the test assembly loaded: the count the run is compared with.</summary>
    public static int ClaudeBefore { get; private set; } = -1;

    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable(Flag, "1");
        ClaudeBefore = ClaudeCount();
    }

    /// <summary>The number of running claude.exe processes now. Read-only: the processes are listed and disposed, never stopped.</summary>
    public static int ClaudeCount()
    {
        var processes = Process.GetProcessesByName("claude");
        try
        {
            return processes.Length;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }
}
