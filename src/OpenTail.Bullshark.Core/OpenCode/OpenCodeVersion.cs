using System.Text.Json;

namespace OpenTail.Bullshark.Core.OpenCode;

/// <summary>
/// The opencode version the app reads (OC-D1). The version comes from the npm package file beside the shim, so no program is started. Pure functions
/// only: no process and no network.
/// </summary>
public static class OpenCodeVersion
{
    /// <summary>The build the app was read against: the npm CLI 1.4.x. A version that starts with <c>1.4.</c> is Ok (OC-D1).</summary>
    public const string Line = "1.4";

    /// <summary>
    /// The <c>version</c> string of an npm package.json. Null when the text is not JSON (the JSON error is caught, not thrown), is not an object, has no
    /// <c>version</c>, or the version is not a string or is blank.
    /// </summary>
    public static string? FromPackageJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.String) return null;
            var text = version.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The package file beside a shim: <c>&lt;the directory of shimPath&gt;\node_modules\opencode-ai\package.json</c>. The npm install keeps its shim
    /// (<c>opencode.cmd</c>) in the folder that holds <c>node_modules</c>. Nothing is read here.
    /// </summary>
    public static string PackagePathBeside(string shimPath) =>
        Path.Combine(Path.GetDirectoryName(shimPath) ?? string.Empty, "node_modules", "opencode-ai", "package.json");
}
