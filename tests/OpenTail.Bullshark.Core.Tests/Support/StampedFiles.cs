namespace OpenTail.Bullshark.Core.Tests.Support;

/// <summary>Test files with a known write time (dedup review item 4, 2026-11-23). The launcher tests age their files with it.</summary>
public static class StampedFiles
{
    /// <summary>
    /// Writes a small file into the folder (the folder is made if it is missing), and sets its write time in UTC. Returns the file's path. The text
    /// defaults to a synthetic line; a test that wants another text passes it.
    /// </summary>
    public static string Write(string folder, string name, DateTimeOffset written, string text = "synthetic")
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, written.UtcDateTime);
        return path;
    }
}
