using System.IO;
using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// A scratch transcript for the views' tests (perf and testability batch, Q7): a folder of its own under the temp folder, with one session of user
/// messages, each one line. The history index reads it the way it reads the owner's transcripts, so the digest is the app's own.
/// </summary>
internal sealed class TranscriptFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-app-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>The session's file.</summary>
    public string FilePath { get; }

    /// <summary>The session's digest, as the history index gives it.</summary>
    public SessionDigest Digest { get; }

    /// <summary>
    /// Writes <paramref name="records"/> user messages; message <c>n</c> (one-based) reads <paramref name="textOf"/>(n), so a test can put a word in
    /// chosen lines.
    /// </summary>
    public TranscriptFixture(int records, Func<int, string> textOf)
    {
        var project = Path.Combine(_root, "projects", "C--fixture");
        Directory.CreateDirectory(project);
        FilePath = Path.Combine(project, Guid.NewGuid().ToString() + ".jsonl");
        var lines = Enumerable.Range(1, records)
            .Select(n => "{\"type\":\"user\",\"timestamp\":\"2026-11-19T08:00:00.000Z\",\"message\":{\"role\":\"user\",\"content\":\"" + textOf(n) + "\"}}");
        File.WriteAllLines(FilePath, lines);
        var index = TranscriptIndex.Build(Path.Combine(_root, "projects"), Path.Combine(_root, "cache", "index.json"));
        Digest = index.Sessions.Single(session => string.Equals(session.FilePath, FilePath, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
