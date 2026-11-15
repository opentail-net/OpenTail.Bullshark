using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Resume;

/// <summary>Reads a past session for resuming: every line as a person reads it, and the folder the session ran in.</summary>
public static class ResumeSource
{
    private const int MaxLinesToScanForFolder = 2000;
    private const int PageSize = 500;

    /// <summary>The session's lines, oldest first, and its working folder (null when no record names one).</summary>
    public static (IReadOnlyList<TranscriptLine> Lines, string? WorkingDirectory) Load(string path)
    {
        var pager = TranscriptPager.Open(path);
        var lines = new List<TranscriptLine>();
        for (var start = 0; start < pager.LineCount; start += PageSize)
            lines.AddRange(pager.ReadPage(start, Math.Min(PageSize, pager.LineCount - start)));
        return (lines, WorkingDirectoryOf(path));
    }

    /// <summary>The folder the session ran in: the first "cwd" in its records, or null when none is found near the start.</summary>
    public static string? WorkingDirectoryOf(string path)
    {
        using var reader = new StreamReader(path);
        for (var i = 0; i < MaxLinesToScanForFolder; i++)
        {
            var json = reader.ReadLine();
            if (json is null) break;
            if (!json.Contains("\"cwd\"", StringComparison.Ordinal)) continue;
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("cwd", out var cwd)
                    && cwd.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(cwd.GetString()))
                    return cwd.GetString();
            }
            catch (JsonException)
            {
                // A record that is not JSON is skipped; the next one may name the folder.
            }
        }
        return null;
    }
}