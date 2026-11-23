using System;
using System.IO;
using System.Threading.Tasks;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The two file writes the Codex and Copilot resume screens make for the window (dedup review, 2026-11-23): the summary file, and the removal of start
/// files that have gone old. The two screens had the same code; it lives here once. The log lines keep each provider's name, passed as the label, so
/// what the log says does not change.
/// </summary>
internal static class ResumeStartFiles
{
    /// <summary>
    /// Saves a summary as a file beside the state file. Returns false, with a log line, when the file cannot be written: nothing is thrown.
    /// </summary>
    public static async Task<bool> SaveSummaryAsync(
        string statePath, string logLabel, string title, SummaryLevel level, string sessionId, string summary)
    {
        var folder = SummaryFiles.FolderBeside(statePath);
        try
        {
            await Task.Run(() => SummaryFiles.Save(folder, title, level, sessionId, summary, DateTimeOffset.Now));
            AppLog.Write($"{logLabel} summary saved as a file: {summary.Length:N0} characters");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"{logLabel} summary not saved as a file: {ex.GetType().Name}");
            return false;
        }
    }

    /// <summary>
    /// Removes the start files that have gone old, and logs how many were removed. <paramref name="startsFolderBeside"/> gives the folder that goes with the
    /// state file; <paramref name="removeOld"/> removes from that folder. Nothing is thrown.
    /// </summary>
    public static void RemoveOldStartsQuietly(
        string statePath, string logLabel, Func<string, string> startsFolderBeside, Func<string, int> removeOld)
    {
        try
        {
            var removed = removeOld(startsFolderBeside(statePath));
            if (removed > 0) AppLog.Write($"old {logLabel} start files removed: {removed}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"old {logLabel} start files not removed: {ex.GetType().Name}");
        }
    }
}
