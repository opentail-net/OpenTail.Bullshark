using OpenTail.Bullshark.Core.Health;
using OpenTail.Bullshark.Core.OpenCode;

namespace OpenTail.Bullshark.Core.Tests.OpenCode;

/// <summary>
/// Stage OC1a (OC-D1, OC-D17): the four rows of the opencode setup check, with the exact texts. The file reader is a fake; no process is started and no
/// PATH is searched.
/// </summary>
public sealed class OpenCodeSetupCheckTests
{
    private const string Shim = "C:\\Fixture\\npm\\opencode.cmd";
    private const string PackageFile = "C:\\Fixture\\npm\\node_modules\\opencode-ai\\package.json";

    private static string Package(string version) => $"{{\"name\": \"opencode-ai\", \"version\": \"{version}\"}}";

    /// <summary>A reader that returns <paramref name="json"/> for the package file beside the shim, and null for any other path.</summary>
    private static Func<string, string?> ReaderOf(string? json) => path => path == PackageFile ? json : null;

    private static SetupItem Only(IReadOnlyList<SetupItem> rows)
    {
        Assert.Single(rows);
        return rows[0];
    }

    [Fact]
    public void WithNoShimTheRowIsTheProblemOfNotOnPath()
    {
        var row = Only(OpenCodeSetupCheck.ForFound(null, ReaderOf(null)));

        Assert.Equal("opencode CLI", row.Area);
        Assert.Equal("opencode", row.Name);
        Assert.Equal(SetupState.Problem, row.State);
        Assert.Equal("opencode is not on PATH: install the opencode CLI (npm), or add it to PATH, then press Check again", row.Detail);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankShimIsTheSameProblem(string shim)
    {
        var row = Only(OpenCodeSetupCheck.ForFound(shim, ReaderOf(null)));

        Assert.Equal(SetupState.Problem, row.State);
        Assert.Equal("opencode is not on PATH: install the opencode CLI (npm), or add it to PATH, then press Check again", row.Detail);
    }

    [Fact]
    public void ANoShimReadsNoFile()
    {
        var asked = new List<string>();
        OpenCodeSetupCheck.ForFound(null, path => { asked.Add(path); return null; });

        Assert.Empty(asked);
    }

    [Fact]
    public void APackageFileThatCannotBeReadIsAWarningThatThisIsNotTheNpmInstall()
    {
        var row = Only(OpenCodeSetupCheck.ForFound(Shim, ReaderOf(null)));

        Assert.Equal(SetupState.Warning, row.State);
        Assert.Equal("version not read: this is not the npm install; found: C:\\Fixture\\npm\\opencode.cmd", row.Detail);
    }

    [Fact]
    public void ThePackageFileReadIsTheOneBesideTheShim()
    {
        var asked = new List<string>();
        OpenCodeSetupCheck.ForFound(Shim, path => { asked.Add(path); return Package("1.4.3"); });

        Assert.Equal(new[] { PackageFile }, asked);
    }

    [Fact]
    public void APackageFileWithNoVersionIsTheSameWarning()
    {
        var row = Only(OpenCodeSetupCheck.ForFound(Shim, ReaderOf("{\"name\": \"opencode-ai\"}")));

        Assert.Equal(SetupState.Warning, row.State);
        Assert.Equal("version not read: this is not the npm install; found: C:\\Fixture\\npm\\opencode.cmd", row.Detail);
    }

    [Fact]
    public void APackageFileThatIsNotJsonIsTheSameWarning()
    {
        var row = Only(OpenCodeSetupCheck.ForFound(Shim, ReaderOf("{ broken")));

        Assert.Equal(SetupState.Warning, row.State);
        Assert.Equal("version not read: this is not the npm install; found: C:\\Fixture\\npm\\opencode.cmd", row.Detail);
    }

    [Fact]
    public void AReaderThatThrowsIsTreatedAsNotRead()
    {
        var row = Only(OpenCodeSetupCheck.ForFound(Shim, _ => throw new IOException("denied")));

        Assert.Equal(SetupState.Warning, row.State);
        Assert.Equal("version not read: this is not the npm install; found: C:\\Fixture\\npm\\opencode.cmd", row.Detail);
    }

    [Theory]
    [InlineData("1.4.3")]
    [InlineData("1.4.0")]
    [InlineData("1.4.12")]
    public void A14XVersionIsOk(string version)
    {
        var row = Only(OpenCodeSetupCheck.ForFound(Shim, ReaderOf(Package(version))));

        Assert.Equal(SetupState.Ok, row.State);
        Assert.Equal(
            $"opencode version {version} (read from the package file, no program was started); found: C:\\Fixture\\npm\\opencode.cmd",
            row.Detail);
    }

    [Theory]
    [InlineData("1.3.11")]
    [InlineData("1.40.0")]
    [InlineData("2.0.0")]
    [InlineData("1.4")]
    public void AnyOtherVersionIsAWarningThatNamesIt(string version)
    {
        var row = Only(OpenCodeSetupCheck.ForFound(Shim, ReaderOf(Package(version))));

        Assert.Equal(SetupState.Warning, row.State);
        Assert.Equal(
            $"opencode version {version} was not the version the app was read against (1.4.x); its data may differ; found: C:\\Fixture\\npm\\opencode.cmd",
            row.Detail);
    }

    [Fact]
    public void TheDetailCarriesThePathOfTheShim()
    {
        var row = Only(OpenCodeSetupCheck.ForFound(Shim, ReaderOf(Package("1.4.3"))));

        Assert.Contains(Shim, row.Detail);
    }

    [Fact]
    public void ALongPathIsClippedAtThe300CharacterLimitWithAnEllipsis()
    {
        var longShim = "C:\\" + new string('a', 400) + "\\opencode.cmd";
        var longPackage = Path.Combine(Path.GetDirectoryName(longShim)!, "node_modules", "opencode-ai", "package.json");
        var row = Only(OpenCodeSetupCheck.ForFound(longShim, path => path == longPackage ? Package("1.4.3") : null));

        // SetupCheck.Clip keeps 300 characters and adds one ellipsis character, the same as the Copilot rows.
        Assert.Equal(SetupState.Ok, row.State);
        Assert.Equal(301, row.Detail.Length);
        Assert.EndsWith("…", row.Detail);
    }

    [Theory]
    [InlineData("1.4.3")]
    [InlineData("1.3.11")]
    [InlineData("")]
    public void NoRowIsAProblemExceptTheMissingShim(string version)
    {
        var rows = OpenCodeSetupCheck.ForFound(Shim, ReaderOf(string.IsNullOrEmpty(version) ? null : Package(version)));

        Assert.All(rows, row => Assert.NotEqual(SetupState.Problem, row.State));
    }
}
