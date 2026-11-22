namespace OpenTail.Bullshark.App;

/// <summary>
/// Runs a task that nothing awaits, and logs what it throws (reliability batch R10). Without this, an exception in such a task is lost: no
/// await sees it and no handler is called. The log gets the exception's type only: not its message (a message can carry a file path) and not its stack
/// (the stack is not written, as the paths in it would be).
/// </summary>
internal static class FireAndForget
{
    /// <summary>Logs "<paramref name="what"/> failed: type" when <paramref name="task"/> faults. A task that completes is not logged.</summary>
    public static void Observe(Task task, string what) => task.ContinueWith(
        finished => AppLog.Write($"{what} failed: {Describe(finished.Exception)}"),
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

    private static string Describe(AggregateException? exception)
    {
        var inner = exception?.GetBaseException();
        return inner is null ? "unknown error" : inner.GetType().Name;
    }
}
