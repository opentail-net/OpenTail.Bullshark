using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using OpenTail.Bullshark.Core.Diagnostics;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The crash file for an error the app kept going past (firstrun batch B3): <c>logs\crash-yyyyMMdd-HHmmss.txt</c> beside app.log, redacted by
/// <see cref="Redactor"/>, the newest ten kept. Called from the unhandled-error handlers in App. It never throws: a handler must not fail while it
/// reports a failure. The window shows the one-line notice on its status line (<see cref="TakeNotice"/>).
/// </summary>
public static class CrashFiles
{
    private static readonly object Gate = new();
    private static string? _pending;

    /// <summary>The notice the window has not taken yet, or null. Taking it clears it.</summary>
    public static string? TakeNotice()
    {
        lock (Gate)
        {
            var notice = _pending;
            _pending = null;
            return notice;
        }
    }

    /// <summary>
    /// Writes the crash file for an exception and keeps the newest files. Returns the file's name, or null when it could not be written (the log
    /// has the exception either way, since the caller writes it).
    /// </summary>
    public static string? Record(Exception? exception, string where)
    {
        try
        {
            var folder = Path.GetDirectoryName(AppLog.FilePath)!;
            Directory.CreateDirectory(folder);
            var at = DateTimeOffset.Now;
            var name = KeptFiles.UniqueName(folder, CrashReport.FileNameFor(at));
            var text = CrashReport.Build(new CrashInput(
                At: at,
                AppVersion: AppInfo.VersionLine(claudeVersion: null),
                OperatingSystem: RuntimeInformation.OSDescription,
                Runtime: RuntimeInformation.FrameworkDescription,
                Exception: exception ?? new InvalidOperationException(where),
                LogLines: DiagnosticsSources.LastLines(AppLog.FilePath, CrashReport.LogLinesKept),
                UserProfile: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
            File.WriteAllText(Path.Combine(folder, name), text);
            KeptFiles.KeepNewest(folder, CrashReport.Pattern, CrashReport.KeepNewest, AppLog.Write);
            lock (Gate) _pending = $"The app hit an error and kept going: logs\\{name}";
            return name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            AppLog.Write($"crash file not written ({where}): {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>
    /// Shows the notice on the open window's status line, now, when there is a window to show it in. The window keeps it on its status line
    /// after this; with no window it waits until one takes it. Called on the UI thread.
    /// </summary>
    public static void ShowOnWindow()
    {
        if (Application.Current?.MainWindow is SessionWindow window && TakeNotice() is { } notice) window.ShowCrashNotice(notice);
    }
}

/// <summary>The app's own version, read from the assembly (firstrun batch B5).</summary>
public static class AppInfo
{
    /// <summary>The version and the git hash the assembly was built with, split at the plus sign.</summary>
    public static (string Version, string? Hash) Version() =>
        OpenTail.Bullshark.Core.Diagnostics.AppVersion.Split(
            System.Reflection.Assembly.GetEntryAssembly()?
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion);

    /// <summary>"Version 0.1.0 (abc1234) · .NET 10", with the claude version when one is given.</summary>
    public static string VersionLine(string? claudeVersion)
    {
        var (version, hash) = Version();
        return OpenTail.Bullshark.Core.Diagnostics.AppVersion.Line(version, hash, Environment.Version.Major, claudeVersion);
    }
}
