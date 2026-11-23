using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Behaviour;
using OpenTail.Bullshark.Core.Cache;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Folders;
using OpenTail.Bullshark.Core.Health;
using OpenTail.Bullshark.Core.OpenCode;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Storage;
using OpenTail.Bullshark.Core.Trust;
using OpenTail.Bullshark.Core.Ui;
using Wpf.Ui.Terminal;

namespace OpenTail.Bullshark.App;

/// <summary>True when the process is a test host (the test runner), so code that starts or resumes real sessions can refuse to run there.</summary>
internal static class RunningUnderTest
{
    /// <summary>True when this process is the test runner's own host (testhost), whether or not the flag is set.</summary>
    public static bool IsTestHost { get; } =
        System.Diagnostics.Process.GetCurrentProcess().ProcessName.StartsWith("testhost", StringComparison.OrdinalIgnoreCase)
        || (System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name?.StartsWith("testhost", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>True when the OPENTAIL_BULLSHARK_UNDER_TEST flag is set to 1 in the environment (the test guard sets it).</summary>
    public static bool FlagSet { get; } =
        string.Equals(Environment.GetEnvironmentVariable("OPENTAIL_BULLSHARK_UNDER_TEST"), "1", StringComparison.Ordinal);

    /// <summary>True in a test host, or when the flag is set. A window that would start sessions refuses to run then.</summary>
    public static bool Is => FlagSet || IsTestHost;
}
