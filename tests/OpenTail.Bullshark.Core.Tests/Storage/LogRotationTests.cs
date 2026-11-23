using OpenTail.Bullshark.Core.Storage;

namespace OpenTail.Bullshark.Core.Tests.Storage;

/// <summary>The shared log rotation rule (dedup review item 5, 2026-11-23): the size check, the replacement, and the failure that is returned.</summary>
public sealed class LogRotationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "opentail-rotation-" + Guid.NewGuid().ToString("N"));

    public LogRotationTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void AFileBelowTheLimitIsLeftAsItIs()
    {
        var path = Path.Combine(_folder, "app.log");
        File.WriteAllText(path, "123456789");

        var failure = LogRotation.RotateIfAtLeast(path, path + ".1", 10);

        Assert.Null(failure);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".1"));
    }

    [Fact]
    public void AFileAtTheLimitMovesAndReplacesTheRotatedCopy()
    {
        var path = Path.Combine(_folder, "app.log");
        File.WriteAllText(path + ".1", "older copy");
        File.WriteAllText(path, "1234567890");

        var failure = LogRotation.RotateIfAtLeast(path, path + ".1", 10);

        Assert.Null(failure);
        Assert.False(File.Exists(path));
        Assert.Equal("1234567890", File.ReadAllText(path + ".1"));
    }

    [Fact]
    public void AMissingFileIsNotDue()
    {
        var path = Path.Combine(_folder, "missing.log");

        var failure = LogRotation.RotateIfAtLeast(path, path + ".1", 10);

        Assert.Null(failure);
        Assert.False(File.Exists(path + ".1"));
    }

    [Fact]
    public void AMoveThatFailsIsReturnedAndTheFileStaysWhereItWas()
    {
        var path = Path.Combine(_folder, "held.log");
        File.WriteAllText(path, "1234567890");

        // Another program holds the file with no sharing, so the move cannot take it.
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var failure = LogRotation.RotateIfAtLeast(path, path + ".1", 10);

            Assert.NotNull(failure);
            Assert.True(failure is IOException or UnauthorizedAccessException);
        }
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".1"));
    }
}
