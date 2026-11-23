using System.Diagnostics;
using System.Management;
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

    /// <summary>The number of claude.exe processes this run started when the test assembly loaded: the count the run is compared with.</summary>
    public static int ClaudeBefore { get; private set; } = -1;

    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable(Flag, "1");
        ClaudeBefore = ClaudeCount();
    }

    /// <summary>
    /// The number of claude.exe processes now that this test host started, directly or through another of its processes. A Claude session the owner
    /// starts on the same machine during the run is not counted, so it cannot fail the run (the continue plan, 2026-11-23). Read-only: the process list
    /// is read and nothing is stopped.
    /// </summary>
    public static int ClaudeCount()
    {
        var children = new Dictionary<int, List<int>>();
        var claude = new HashSet<int>();
        using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, Name FROM Win32_Process"))
        using (var all = searcher.Get())
        {
            foreach (var item in all)
            {
                var process = (ManagementObject)item;
                var id = Convert.ToInt32(process["ProcessId"]);
                var parent = Convert.ToInt32(process["ParentProcessId"]);
                if (string.Equals(process["Name"] as string, "claude.exe", StringComparison.OrdinalIgnoreCase)) claude.Add(id);
                if (!children.TryGetValue(parent, out var list)) children[parent] = list = new List<int>();
                list.Add(id);
                process.Dispose();
            }
        }

        var count = 0;
        var seen = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(Environment.ProcessId);
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            if (!children.TryGetValue(parent, out var kids)) continue;
            foreach (var kid in kids)
            {
                if (!seen.Add(kid)) continue;
                if (claude.Contains(kid)) count++;
                queue.Enqueue(kid);
            }
        }
        return count;
    }
}
