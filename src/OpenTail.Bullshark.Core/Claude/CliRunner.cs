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
    private readonly string _executable;

    public ProcessCliRunner(string executable = "claude")
    {
        _executable = executable;
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

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

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
            return new CliResult(-1, await stdout, await stderr, TimedOut: true);
        }

        return new CliResult(process.ExitCode, await stdout, await stderr, TimedOut: false);
    }
}
