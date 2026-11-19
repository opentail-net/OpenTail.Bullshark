using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenTail.Bullshark.Core.Diagnostics;
using OpenTail.Bullshark.Core.Health;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Ui;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The window's side of the crash notice and the diagnostics zip (firstrun batch B3 and B4). The zip is written into <c>diagnostics\</c> beside the
/// state file and its folder is opened; nothing is sent, and the app makes no network call. The Claude version is the one a pass has already read
/// (<see cref="_cliVersion"/>), and the preflight is the one the Night tab last drew (<see cref="_lastPreflight"/>): neither starts anything.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The crash notice on the status line, kept until the window closes (firstrun batch B3).</summary>
    private string? _crashNotice;

    /// <summary>The installed claude's version, as a pass last read it. Null until a pass has read it.</summary>
    private string? _cliVersion;

    /// <summary>The preflight the Night tab last drew. Null until the Night tab has been read in this session.</summary>
    private PreflightReport? _lastPreflight;

    /// <summary>Shows a crash notice on the status line, and keeps it there (called by <see cref="CrashFiles.ShowOnWindow"/>).</summary>
    public void ShowCrashNotice(string notice)
    {
        _crashNotice = notice;
        SetStatus(notice);
    }

    /// <summary>
    /// Settings > Claude > Save diagnostics… (firstrun batch B4). Writes the zip, keeps the newest five, and opens its folder. A failure is said on
    /// the status line; the log has the type of the failure.
    /// </summary>
    private void SaveDiagnostics()
    {
        var stateFile = _options.StatePath;
        var stateFolder = Path.GetDirectoryName(Path.GetFullPath(stateFile))!;
        try
        {
            var input = new DiagnosticsInput(
                VersionsText: VersionsText(),
                SetupText: DiagnosticsBundle.SetupText(SetupCheck.Run(SetupCheck.DefaultClaudeDir, SetupCheck.DefaultClaudeJsonPath)),
                PreflightText: _lastPreflight is { } report
                    ? DiagnosticsBundle.PreflightText(report)
                    : "not run in this session: open Insights, Night, then save again",
                AppLogLines: DiagnosticsSources.LastLines(AppLog.FilePath, DiagnosticsBundle.AppLogLinesKept),
                EventLines: DiagnosticsSources.EventLines(Path.Combine(stateFolder, "events.jsonl"), DiagnosticsBundle.EventsKept),
                SettingsText: DiagnosticsSources.SettingsText(UiSettingsFile.PathFor(stateFile)));
            var path = DiagnosticsBundle.Save(stateFolder, DateTimeOffset.Now, input,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), AppLog.Write);
            AppLog.Write($"diagnostics saved: {Path.GetFileName(path)} (the owner's choice in Settings; nothing sent)");
            SetStatus($"Diagnostics saved to diagnostics\\{Path.GetFileName(path)}. Nothing was sent. Read it before you share it.");
            OpenFolder(Path.GetDirectoryName(path)!);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"diagnostics not saved: {ex.GetType().Name}: {ex.Message}");
            SetStatus($"Diagnostics not saved: {PassHealth.Short(ex.Message)}");
        }
    }

    /// <summary>The versions file of the zip: the app version and git hash, .NET, the OS, and the claude version when a pass has read it.</summary>
    private string VersionsText()
    {
        var (version, hash) = AppInfo.Version();
        return string.Join(Environment.NewLine,
            $"App: {version}{(hash is null ? string.Empty : $" ({hash})")}",
            $"Runtime: {RuntimeInformation.FrameworkDescription}",
            $"OS: {RuntimeInformation.OSDescription}",
            $"Claude Code: {_cliVersion ?? "not read in this session"}");
    }

    /// <summary>Opens a folder in Explorer. A folder that cannot be opened is logged; the zip is saved either way.</summary>
    private static void OpenFolder(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            AppLog.Write($"folder not opened: {ex.GetType().Name}");
        }
    }
}
