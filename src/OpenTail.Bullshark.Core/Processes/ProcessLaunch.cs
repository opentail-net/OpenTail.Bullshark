using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace OpenTail.Bullshark.Core.Processes;

/// <summary>
/// The set-up and the stop that the process runners share (dedup plan 2026-11-23). Each runner still starts its own process and keeps its own
/// failure convention (a throw, a result, or a reading); this class does not start processes and does not catch a failed start.
/// </summary>
public static class ProcessLaunch
{
    /// <summary>
    /// The start information for a program with its output redirected and no window. The output (and error) encoding is set only when given,
    /// and the input encoding only when standard input is redirected: <see cref="ProcessStartInfo"/> refuses an input encoding otherwise.
    /// The caller adds any environment variables it needs, and sets the working folder when it has one (a null folder is left unset).
    /// </summary>
    public static ProcessStartInfo Create(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        bool redirectStandardInput = false,
        Encoding? outputEncoding = null,
        Encoding? inputEncoding = null)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectStandardInput,
        };
        if (workingDirectory is not null) info.WorkingDirectory = workingDirectory;
        if (outputEncoding is not null)
        {
            info.StandardOutputEncoding = outputEncoding;
            info.StandardErrorEncoding = outputEncoding;
        }
        if (redirectStandardInput && inputEncoding is not null) info.StandardInputEncoding = inputEncoding;
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        return info;
    }

    /// <summary>
    /// Stops the process and whatever it started. A process that has already exited is left as it is. A stop that fails because the process ended
    /// in the meantime, or could not be stopped, is not an error: the caller's output or exit code says what happened.
    /// </summary>
    public static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // It ended on its own between the check and the kill.
        }
        catch (Win32Exception)
        {
            // It could not be stopped; it is left to end on its own.
        }
    }
}
