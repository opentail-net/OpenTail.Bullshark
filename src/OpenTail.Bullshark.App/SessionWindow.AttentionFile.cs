using System.IO;
using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Ui;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The attention file for other tools (firstrun batch B9): off by default, switched on in Settings > Claude. When it is on, the supervising window
/// writes <c>attention.json</c> beside the state file on each pass, and deletes it on a clean close. Only the reason codes and the session names are
/// written. Nothing is sent, and nothing reads the file back.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The attention file beside the state file.</summary>
    private string AttentionFilePath => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_options.StatePath))!, AttentionFile.FileName);

    /// <summary>The Settings switch: writes the key, deletes the file when it is turned off, and draws what the file holds afterwards.</summary>
    private void SetAttentionFile(bool on)
    {
        UiFile.Set(_options.StatePath, AttentionFile.Key, JsonValue.Create(on)!);
        var now = AttentionFile.Enabled(UiFile.Read(_options.StatePath, AttentionFile.Key));
        if (!now) DeleteAttentionFile();
        AppLog.Write($"attention file: {(now ? "on" : "off")} (the owner's choice in Settings)");
        SetStatus(now == on
            ? now ? "attention.json is written on each pass, beside the state file." : "attention.json is off; the file is deleted."
            : "Not saved: ui.json cannot be written. See the log.");
        RefreshSettingsPage();
    }

    /// <summary>
    /// Writes the file for this pass when the switch is on (firstrun batch B9). Only the supervising window writes it. A file that cannot be written is
    /// logged, and the pass goes on.
    /// </summary>
    private void WriteAttentionFile()
    {
        if (_watching || _supervisor is null) return;
        // The switch is the pass's copy of ui.json (perf and testability batch, Q2): no read on the UI thread here.
        if (!_passSettings.AttentionFile)
        {
            DeleteAttentionFile();
            return;
        }
        try
        {
            AttentionFile.Write(AttentionFilePath, AttentionFile.Build(DateTimeOffset.UtcNow, _attention));
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"attention file not written: {ex.GetType().Name}");
        }
    }

    /// <summary>Deletes the attention file, if there is one (a clean close, or the switch turned off).</summary>
    private void DeleteAttentionFile() => AttentionFile.Delete(AttentionFilePath);
}
