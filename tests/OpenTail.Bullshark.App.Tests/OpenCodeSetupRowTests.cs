using System.IO;
using OpenTail.Bullshark.App;
using OpenTail.Bullshark.Core.Health;
using OpenTail.Bullshark.Core.OpenCode;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// Stage OC2e (Part 2): the opencode row of the setup check, as the App builds it. Most tests pass a fake shim path and a fake file reader; the last ones
/// read real temp files through the App's reader. No program is started and nothing is logged.
/// </summary>
public sealed class OpenCodeSetupRowTests
{
    private const string Shim = "C:\\Fixture\\npm\\opencode.cmd";

    private static readonly string PackageFile = OpenCodeVersion.PackagePathBeside(Shim);

    private static string Package(string version) => $"{{\"name\": \"opencode-ai\", \"version\": \"{version}\"}}";

    /// <summary>A reader that returns <paramref name="json"/> for the package file beside the shim, and null for any other path.</summary>
    private static Func<string, string?> ReaderOf(string? json) => path => path == PackageFile ? json : null;

    private sealed class TempFolder : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "bullshark-opencode-row-" + Guid.NewGuid().ToString("N"));

        public TempFolder() => Directory.CreateDirectory(Folder);

        public void Dispose()
        {
            if (!Directory.Exists(Folder)) return;
            try { Directory.Delete(Folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void WithNoShimTheRowIsTheProblemAndNothingIsRead()
    {
        var asked = new List<string>();

        var row = Assert.Single(SessionWindow.OpenCodeRows(null, path => { asked.Add(path); return null; }));

        Assert.Equal("opencode CLI", row.Area);
        Assert.Equal("opencode", row.Name);
        Assert.Equal(SetupState.Problem, row.State);
        Assert.Empty(asked);
    }

    [Fact]
    public void AVersionOnThe14LineIsOkAndTheShimIsInTheDetail()
    {
        var row = Assert.Single(SessionWindow.OpenCodeRows(Shim, ReaderOf(Package("1.4.3"))));

        Assert.Equal(SetupState.Ok, row.State);
        Assert.Contains("1.4.3", row.Detail);
        Assert.Contains(Shim, row.Detail);
    }

    [Fact]
    public void AVersionOffThe14LineIsAWarning()
    {
        var row = Assert.Single(SessionWindow.OpenCodeRows(Shim, ReaderOf(Package("1.5.0"))));

        Assert.Equal(SetupState.Warning, row.State);
    }

    [Fact]
    public void APackageFileThatIsNotReadIsAWarningThatThisIsNotTheNpmInstall()
    {
        var row = Assert.Single(SessionWindow.OpenCodeRows(Shim, ReaderOf(null)));

        Assert.Equal(SetupState.Warning, row.State);
        Assert.StartsWith("version not read: this is not the npm install", row.Detail);
    }

    [Fact]
    public void ReadPackageTextReturnsTheTextOfAFile()
    {
        using var temp = new TempFolder();
        var file = Path.Combine(temp.Folder, "package.json");
        File.WriteAllText(file, Package("1.4.0"));

        Assert.Equal(Package("1.4.0"), SessionWindow.ReadPackageText(file));
    }

    [Fact]
    public void ReadPackageTextReturnsNullForAMissingFile()
    {
        using var temp = new TempFolder();

        Assert.Null(SessionWindow.ReadPackageText(Path.Combine(temp.Folder, "missing.json")));
    }

    [Fact]
    public void ReadPackageTextReturnsNullForAFolderWhereAFileIsExpected()
    {
        using var temp = new TempFolder();

        Assert.Null(SessionWindow.ReadPackageText(temp.Folder));
    }

    [Fact]
    public void TheRealPackageFileBesideATempShimGivesTheOkRow()
    {
        using var temp = new TempFolder();
        var shim = Path.Combine(temp.Folder, "opencode.cmd");
        var packageDirectory = Path.Combine(temp.Folder, "node_modules", "opencode-ai");
        Directory.CreateDirectory(packageDirectory);
        File.WriteAllText(Path.Combine(packageDirectory, "package.json"), Package("1.4.7"));

        var row = Assert.Single(SessionWindow.OpenCodeRows(shim, SessionWindow.ReadPackageText));

        Assert.Equal(SetupState.Ok, row.State);
        Assert.Contains("1.4.7", row.Detail);
    }

    [Fact]
    public void TheRealShimWithNoPackageFileIsTheNpmWarning()
    {
        using var temp = new TempFolder();
        var shim = Path.Combine(temp.Folder, "opencode.cmd");

        var row = Assert.Single(SessionWindow.OpenCodeRows(shim, SessionWindow.ReadPackageText));

        Assert.Equal(SetupState.Warning, row.State);
        Assert.StartsWith("version not read: this is not the npm install", row.Detail);
    }
}
