using System.Text.Json.Nodes;
using System.Windows;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The Settings tab (Insights and Settings plan, 2026-11-18). Every choice writes the key it always wrote, in ui.json through <see cref="UiFile"/>,
/// and then draws what the file holds (a file that cannot be written shows its old value and a status line). The keys and their readers are
/// unchanged: "notify", "toast", "magicAuto", "escalateMinutes" and "railTheme". The zoom stays in the status bar. Nothing here is sent to a session.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The key of the Settings tab in the tab strip.</summary>
    private const string SettingsKey = "settings";

    private readonly SettingsView _settings = new() { Visibility = Visibility.Hidden };

    private void InitSettings()
    {
        Pane.Children.Add(_settings);
        _settings.ThemeChosen += SetTheme;
        _settings.NotifyChanged += on => WriteFlag(NotifyKey, on, "Sound and flash for new waits");
        _settings.ToastChanged += on => WriteFlag(ToastKey, on, "Toasts when the window is in the background");
        _settings.MagicAutoChanged += SetMagicAuto;
        _settings.EscalateChosen += SetEscalation;
        _settings.InstructionsRequested += OpenInstructions;
        _settings.SetupCheckRequested += OpenSetupCheck;
    }

    /// <summary>The status bar's Settings button: the sub-tab that was shown last.</summary>
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings(_settings.Current);

    /// <summary>Shows the Settings tab on a sub-tab, and draws its values as the files hold them now.</summary>
    private void OpenSettings(SettingsTab tab)
    {
        SetActive(TabFor(TabKind.Settings, SettingsKey, () => "Settings"));
        ShowOnly(_settings);
        _settings.Show(tab);
        RefreshSettingsPage();
    }

    /// <summary>
    /// Draws the Settings page from what the window holds and what ui.json holds now. The file is read again each time, so a value that a write
    /// did not save is shown as it is in the file.
    /// </summary>
    private void RefreshSettingsPage()
    {
        _settings.ShowState(new SettingsState(
            Theme: _theme,
            Notify: AttentionList.NotificationsOn(UiFile.Read(_options.StatePath, NotifyKey)),
            Toast: AttentionList.NotificationsOn(UiFile.Read(_options.StatePath, ToastKey)),
            MagicAuto: _magicAutoOn,
            Escalation: EscalationChoices.Of(UiFile.Read(_options.StatePath, EscalateKey))));
    }

    /// <summary>
    /// Writes an on-or-off key and reports what the file holds afterwards. A file that cannot be written keeps its old value, and the status line
    /// says the change was not saved, so the page never shows a change the file does not hold.
    /// </summary>
    private void WriteFlag(string key, bool on, string label)
    {
        UiFile.Set(_options.StatePath, key, JsonValue.Create(on)!);
        var now = AttentionList.NotificationsOn(UiFile.Read(_options.StatePath, key));
        AppLog.Write($"{label}: {(now ? "on" : "off")} (the owner's choice in Settings)");
        SetStatus(now == on ? $"{label}: {(now ? "on" : "off")}." : "Not saved: ui.json cannot be written. See the log.");
        RefreshSettingsPage();
    }

    /// <summary>
    /// Sets the rail's theme and saves it. The rail's switch and the Settings page both come here, so the two always agree.
    /// </summary>
    private void SetTheme(RailTheme theme)
    {
        _theme = theme;
        ThemeStore.Save(_options.StatePath, _theme);
        ApplyTheme();
        RefreshRail();
        RefreshSettingsPage();
    }

    /// <summary>
    /// Sets how often an unanswered wait re-alerts, in minutes (0 is Off), and shows the value the file holds afterwards. The engine reads
    /// the same key each pass (<see cref="AttentionEscalation.IntervalOf"/>).
    /// </summary>
    private void SetEscalation(int minutes)
    {
        UiFile.Set(_options.StatePath, EscalateKey, JsonValue.Create(minutes)!);
        var now = EscalationChoices.MinutesOf(UiFile.Read(_options.StatePath, EscalateKey));
        AppLog.Write($"needs you: re-alert interval {now} minutes (the owner's choice in Settings)");
        SetStatus(now != minutes ? "Not saved: ui.json cannot be written. See the log."
            : now == 0 ? "Unanswered waits do not sound again."
            : $"Unanswered waits sound again after {now} minutes.");
        RefreshSettingsPage();
    }
}
