using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace OpenTail.Bullshark.Core.Git;

/// <summary>
/// The outcome of one git command. <see cref="OutputTruncated"/> means the standard output was longer than the limit: it was cut
/// there and the process was stopped, so the exit code says nothing about the output.
/// </summary>
public sealed record GitOutput(int ExitCode, string StdOut, string StdErr, bool TimedOut, bool OutputTruncated)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>Runs git. A seam: the tests pass a fake, the window passes <see cref="ProcessGitRunner"/>.</summary>
public interface IGitRunner
{
    /// <summary>Runs `git -C folder arguments`, stops after <paramref name="timeout"/>, and reads at most <paramref name="maxOutputBytes"/> of standard output.</summary>
    Task<GitOutput> RunAsync(
        IReadOnlyList<string> arguments,
        string folder,
        TimeSpan timeout,
        int maxOutputBytes,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs the `git` on the PATH, to read refs, the index and the working tree. No index lock is taken (GIT_OPTIONAL_LOCKS=0), so `status` does not
/// write the index that the coder session may be using at the same time. No credential prompt can appear (GIT_TERMINAL_PROMPT=0).
/// The one command that writes is `merge-tree --write-tree` (<see cref="MergeCheck"/>): it writes tree objects to the object database, and no ref,
/// index or working-tree file. Git 2.53 has no read-only form of a full merge (`--trivial-merge` merges only trivially), so the claim is not
/// "reading only" for that command.
/// </summary>
public sealed class ProcessGitRunner : IGitRunner
{
    private readonly string _executable;

    public ProcessGitRunner(string executable = "git")
    {
        _executable = executable;
    }

    public async Task<GitOutput> RunAsync(
        IReadOnlyList<string> arguments,
        string folder,
        TimeSpan timeout,
        int maxOutputBytes,
        CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo(_executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // The repository's own config must not run programs here (F3): core.fsmonitor is a program that status runs, and core.hooksPath
        // is pointed at the null device so no hook is found. --no-textconv and --no-ext-diff are passed by the callers.
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("core.fsmonitor=false");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("core.hooksPath=NUL");
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(folder);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (Win32Exception ex)
        {
            return new GitOutput(-1, string.Empty, $"git could not be started: {ex.Message}", TimedOut: false, OutputTruncated: false);
        }
        if (process is null) return new GitOutput(-1, string.Empty, "git could not be started.", TimedOut: false, OutputTruncated: false);

        using (process)
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            limit.CancelAfter(timeout);
            try
            {
                // Standard error is read at the same time, so a full error pipe cannot stop git before it closes standard output.
                var stderr = process.StandardError.ReadToEndAsync(limit.Token);
                var (bytes, truncated) = await ReadUpToAsync(process.StandardOutput.BaseStream, maxOutputBytes, limit.Token);
                if (truncated) Stop(process);
                await process.WaitForExitAsync(limit.Token);
                return new GitOutput(process.ExitCode, Encoding.UTF8.GetString(bytes), await stderr, TimedOut: false, OutputTruncated: truncated);
            }
            catch (OperationCanceledException)
            {
                // The git process and whatever it started are stopped, so nothing is left running.
                Stop(process);
                if (cancellationToken.IsCancellationRequested) throw;
                return new GitOutput(-1, string.Empty, string.Empty, TimedOut: true, OutputTruncated: false);
            }
        }
    }

    /// <summary>Reads up to <paramref name="limit"/> bytes. One byte past the limit is read, so output of exactly the limit is not "truncated".</summary>
    private static async Task<(byte[] Bytes, bool Truncated)> ReadUpToAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var room = limit + 1 - (int)buffer.Length;
            if (room <= 0)
            {
                var all = buffer.ToArray();
                return (all[..limit], true);
            }
            var read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, room)), cancellationToken);
            if (read == 0) return (buffer.ToArray(), false);
            buffer.Write(chunk, 0, read);
        }
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // It ended on its own between the check and the kill.
        }
    }
}
