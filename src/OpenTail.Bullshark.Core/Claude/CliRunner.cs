using System.Diagnostics;
using System.Text;

namespace OpenTail.Bullshark.Core.Claude;

/// <summary>The outcome of one `claude` management command.</summary>
public sealed record CliResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>
/// Runs `claude` management commands headless (agents, logs, stop, rm, --bg launch).
/// This is NOT used for the sessions the owner sees: those run in the embedded terminal (finding F3).
/// </summary>
public interface ICliRunner
{
    Task<CliResult> RunAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        string? standardInput = null);
}

/// <summary>Runs the real `claude` executable with redirected output and no window.</summary>
public sealed class ProcessCliRunner : ICliRunner
{
    /// <summary>How long the output is waited for once the command has exited or been stopped (see the constructor).</summary>
    public static readonly TimeSpan DefaultOutputDrain = TimeSpan.FromSeconds(2);

    private readonly string _executable;
    private readonly TimeSpan _outputDrain;

    /// <param name="executable">The program to run.</param>
    /// <param name="outputDrain">
    /// How long the output is waited for after the command has exited or been stopped. A child that inherited the pipe can keep it open after
    /// the command ends; the call then returns the text read so far, instead of waiting for that child (round 12 item A4). Default: 2 seconds.
    /// </param>
    public ProcessCliRunner(string executable = "claude", TimeSpan? outputDrain = null)
    {
        _executable = executable;
        _outputDrain = outputDrain ?? DefaultOutputDrain;
    }

    public async Task<CliResult> RunAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        string? standardInput = null)
    {
        var info = new ProcessStartInfo(_executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        // Only meaningful when standard input is redirected; ProcessStartInfo refuses it otherwise.
        if (standardInput is not null) info.StandardInputEncoding = Encoding.UTF8;
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"Could not start '{_executable}'.");

        // Both pipes are read while the command runs, into buffers that can be taken at any time: the text read so far is what a
        // call returns when a child keeps a pipe open (see TextAfterDrain).
        var stdout = new PipeText();
        var stderr = new PipeText();
        var stdoutDrain = Drain(process.StandardOutput, stdout);
        var stderrDrain = Drain(process.StandardError, stderr);

        // The timeout covers the whole call, including the standard input: a stuck command could block its writing.
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            if (standardInput is not null)
            {
                // Written in full and then closed, so the command sees the end of its input. The output is already being read.
                try
                {
                    await process.StandardInput.WriteAsync(standardInput.AsMemory(), timeoutSource.Token);
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // The command stopped reading early; its exit code and output say why.
                }
            }

            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            // A timeout or a cancel: the command and whatever it started are stopped, so nothing is left running.
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            if (cancellationToken.IsCancellationRequested) throw;
            return new CliResult(-1, await TextAfterDrain(stdoutDrain, stdout), await TextAfterDrain(stderrDrain, stderr), TimedOut: true);
        }

        return new CliResult(process.ExitCode, await TextAfterDrain(stdoutDrain, stdout), await TextAfterDrain(stderrDrain, stderr), TimedOut: false);
    }

    /// <summary>The text one pipe has delivered so far. A drain writes to it while the command runs; the caller takes a snapshot.</summary>
    private sealed class PipeText
    {
        private readonly StringBuilder _text = new();

        public void Append(ReadOnlySpan<char> chars)
        {
            lock (_text) _text.Append(chars);
        }

        public string Snapshot()
        {
            lock (_text) return _text.ToString();
        }
    }

    /// <summary>Reads a pipe to its end, into <paramref name="into"/>. A pipe closed under the read keeps what was read.</summary>
    private static async Task Drain(TextReader reader, PipeText into)
    {
        var chunk = new char[4096];
        try
        {
            int read;
            while ((read = await reader.ReadAsync(chunk.AsMemory())) > 0) into.Append(chunk.AsSpan(0, read));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The process was disposed while its output was being read: the text read so far stays.
        }
    }

    /// <summary>
    /// The whole text of a pipe once it has ended. The wait for the end is bounded (round 12 item A4), as GitRunner's is: a child of the command
    /// that kept the pipe open would otherwise hold the call for as long as it runs. When the wait runs out, the text read so far is returned.
    /// </summary>
    private async Task<string> TextAfterDrain(Task drain, PipeText text)
    {
        try
        {
            await drain.WaitAsync(_outputDrain);
        }
        catch (TimeoutException)
        {
            // Still open: a child holds the pipe, and what was read is what the call returns.
        }
        return text.Snapshot();
    }
}
