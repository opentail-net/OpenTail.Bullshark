using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Cache;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Ui;

namespace OpenTail.Bullshark.App;

/// <summary>The three sub-tabs of Settings (Insights and Settings plan): General, Naming and Claude.</summary>
public enum SettingsTab
{
    General,
    Naming,
    Claude,
}

/// <summary>
/// What the Settings page shows: the values the window read from ui.json (or holds in memory) after its last write. The page draws these
/// and never reads or writes a file itself. The zoom is not here: it stays in the status bar (owner's decision, 2026-11-18).
/// <see cref="ContextWindow"/> is the window the ctx meter uses; <see cref="ToastEvents"/> is the switch of each kind of toast (toasts by event).
/// </summary>
public sealed record SettingsState(
    RailTheme Theme,
    bool Notify,
    bool Toast,
    bool MagicAuto,
    EscalationChoice Escalation,
    ContextWindowChoice ContextWindow,
    IReadOnlyDictionary<ToastCategory, bool> ToastEvents,
    /// <summary>The quiet hours in ui.json (night batch N4); null when they are off.</summary>
    QuietWindow? Quiet = null);

/// <summary>
/// The Settings page (Insights and Settings plan). A tab with pill sub-tabs: General (theme and notifications), Naming (automatic magic
/// naming, and how often an unanswered wait re-alerts) and Claude (the standing instructions and the setup check, which open their own dialogs).
/// The page only draws a <see cref="SettingsState"/> and raises an event for each change; the window writes the key and draws what the file
/// holds afterwards. Nothing here is sent to a session.
/// </summary>
public sealed class SettingsView : UserControl
{
    private static readonly string[] Headers = ["General", "Naming", "Claude"];
    private static readonly string[] ThemeHeaders = ["Dark", "Light"];

    private readonly PillTabs _tabs = new();
    private readonly PillTabs _theme = new();
    private readonly PillTabs _escalate = new();
    private readonly PillTabs _contextWindow = new();
    private readonly PillTabs _quiet = new();
    private readonly CheckBox _notify;
    private readonly CheckBox _toast;
    private readonly CheckBox _magic;
    private readonly Dictionary<ToastCategory, CheckBox> _toastEvents = new();
    private readonly List<TextBlock> _notes = [];
    private readonly Dictionary<SettingsTab, UIElement> _pages = new();
    private SettingsTab _current = SettingsTab.General;

    public SettingsView()
    {
        _notify = Switch("Sound and flash for new waits");
        _toast = Switch("Toasts when the window is in the background");
        _magic = Switch("Automatic magic naming");
        foreach (var category in Enum.GetValues<ToastCategory>())
        {
            var box = Switch(EventLabel(category));
            _toastEvents[category] = box;
            box.Click += (_, _) => ToastEventChanged?.Invoke(category, box.IsChecked == true);
        }

        _tabs.SetItems(Headers, (int)_current);
        _tabs.Chosen += index => Show((SettingsTab)index);
        _theme.SetItems(ThemeHeaders, 0);
        _theme.Chosen += index => ThemeChosen?.Invoke(index == 1 ? RailTheme.Light : RailTheme.Dark);
        _escalate.SetItems(EscalationChoices.Presets.Select(EscalationChoices.PresetLabel).ToList(), 2);
        _escalate.Chosen += index =>
        {
            if (index < EscalationChoices.Presets.Count) EscalateChosen?.Invoke(EscalationChoices.Presets[index]);
        };
        _contextWindow.SetItems(ContextMeter.Presets.Select(ContextMeter.PresetLabel).ToList(), 0);
        _contextWindow.Chosen += index =>
        {
            if (index < ContextMeter.Presets.Count) ContextWindowChosen?.Invoke(ContextMeter.Presets[index]);
        };
        // Off is the first pill; then the presets. A custom value (set by hand in ui.json) is the last pill, and choosing it changes nothing.
        _quiet.SetItems(QuietLabels(null), 0);
        _quiet.Chosen += index =>
        {
            if (index == 0) QuietChosen?.Invoke(null);
            else if (index <= QuietHours.Presets.Count) QuietChosen?.Invoke(QuietHours.Presets[index - 1]);
        };

        _notify.Click += (_, _) => NotifyChanged?.Invoke(_notify.IsChecked == true);
        _toast.Click += (_, _) => ToastChanged?.Invoke(_toast.IsChecked == true);
        _magic.Click += (_, _) => MagicAutoChanged?.Invoke(_magic.IsChecked == true);

        var general = Page(
            Heading("Theme"),
            Note("Dark or light. The switch at the top of the rail does the same."),
            _theme,
            Heading("Notifications"),
            Note("A new wait sounds, and flashes the taskbar button, until the window is in front."),
            _notify,
            Note("Off: no sound, no flash and no toast."),
            _toast,
            Note("A silent Windows toast when the window is not in front. It needs the sound and flash to be on."),
            Lead("Toasts for:"),
            _toastEvents[ToastCategory.Blocked],
            _toastEvents[ToastCategory.Problems],
            _toastEvents[ToastCategory.Notices],
            _toastEvents[ToastCategory.Ready],
            _toastEvents[ToastCategory.LimitReset],
            Note("A kind that is off gives no toast. Its sound and flash are unchanged. The switches are dimmed while the toast switch above is off."),
            Lead("Quiet hours (local time)"),
            Note("Off by default. In these hours a new wait or a re-alert gives no sound, flash or toast. It still shows in Needs you, the rail, " +
                 "the log and the Away digest, and the re-alert count still runs."),
            _quiet,
            Note("A window that crosses midnight is fine. A window set by hand in ui.json is shown as Custom and is not rewritten here."),
            Heading("Context window for the ctx meter"),
            Note("The window of the model is not read from Claude; this is your choice. The warning mark shows at 80 %."),
            _contextWindow);

        var naming = Page(
            Heading("Automatic magic naming"),
            Note("Off by default. When it is on, a session that has never been named gets a name from Haiku when its cache goes cold, "
                 + "at most three an hour, and only while the window is idle. It uses your usage. The palette and the History mark name the cold sessions on request."),
            _magic,
            Heading("Re-alert an unanswered wait"),
            Note("A session that still waits for you sounds again after this time, up to three times. Acknowledge it or snooze it on its row "
                 + "under Needs you to stop. The default is 15 minutes."),
            _escalate);

        var claude = Page(
            Heading("Standing instructions"),
            Note("Your CLAUDE.md, and the project files of the folders Claude has been trusted in. Nothing is sent to a session."),
            ActionButton("Edit standing instructions…", () => InstructionsRequested?.Invoke()),
            Heading("Setup check"),
            Note("Reads Claude Code's settings, hooks, MCP servers and CLAUDE.md on this computer. It changes nothing."),
            ActionButton("Check setup…", () => SetupCheckRequested?.Invoke()));

        _pages[SettingsTab.General] = general;
        _pages[SettingsTab.Naming] = naming;
        _pages[SettingsTab.Claude] = claude;

        var host = new Grid();
        foreach (var page in _pages.Values)
        {
            page.Visibility = Visibility.Collapsed;
            host.Children.Add(page);
        }

        var root = new DockPanel();
        _tabs.Margin = new Thickness(12, 8, 12, 0);
        DockPanel.SetDock(_tabs, Dock.Top);
        root.Children.Add(_tabs);
        root.Children.Add(new ScrollViewer { Content = host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        Show(_current);
    }

    /// <summary>The events the owner's choices raise. The window writes the key and then calls <see cref="ShowState"/>.</summary>
    public event Action<RailTheme>? ThemeChosen;

    public event Action<bool>? NotifyChanged;

    public event Action<bool>? ToastChanged;

    public event Action<bool>? MagicAutoChanged;

    /// <summary>One kind of toast switched on or off (toasts by event).</summary>
    public event Action<ToastCategory, bool>? ToastEventChanged;

    /// <summary>A context window chosen from the presets, in tokens (ctx meter, Part 1).</summary>
    public event Action<long>? ContextWindowChosen;

    /// <summary>A re-alert interval chosen from the presets, in minutes (0 is Off).</summary>
    public event Action<int>? EscalateChosen;

    /// <summary>Quiet hours chosen: a preset window, or null for Off (night batch N4).</summary>
    public event Action<QuietWindow?>? QuietChosen;

    /// <summary>Edit standing instructions… was pressed.</summary>
    public event Action? InstructionsRequested;

    /// <summary>Check setup… was pressed.</summary>
    public event Action? SetupCheckRequested;

    /// <summary>The sub-tab that is shown, or was shown last.</summary>
    public SettingsTab Current => _current;

    /// <summary>Shows a sub-tab. The values are drawn by <see cref="ShowState"/>, which the window calls.</summary>
    public void Show(SettingsTab tab)
    {
        _current = tab;
        _tabs.Select((int)tab);
        foreach (var (key, page) in _pages) page.Visibility = key == tab ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Draws the values the window read after its last write. Changes made here are not raised by it.</summary>
    public void ShowState(SettingsState state)
    {
        _theme.Select(state.Theme == RailTheme.Light ? 1 : 0);
        _notify.IsChecked = state.Notify;
        _toast.IsChecked = state.Toast;
        _magic.IsChecked = state.MagicAuto;

        var labels = EscalationChoices.Presets.Select(EscalationChoices.PresetLabel).ToList();
        var index = EscalationChoices.Presets.ToList().IndexOf(state.Escalation.Minutes);
        // A value in the file that is not a preset is shown as one more choice, selected, and is not rewritten (EscalationChoices).
        if (state.Escalation.IsCustom)
        {
            labels.Add(state.Escalation.Label);
            index = labels.Count - 1;
        }
        _escalate.SetItems(labels, index);

        // The context window: the presets, and a value in the file that is not one of them, shown selected and not rewritten (ContextMeter).
        var windowLabels = ContextMeter.Presets.Select(ContextMeter.PresetLabel).ToList();
        var windowIndex = ContextMeter.Presets.ToList().IndexOf(state.ContextWindow.Tokens);
        if (state.ContextWindow.IsCustom)
        {
            windowLabels.Add(state.ContextWindow.Label);
            windowIndex = windowLabels.Count - 1;
        }
        _contextWindow.SetItems(windowLabels, windowIndex);

        // The quiet hours: Off, the presets, and a window in the file that is not a preset, shown selected and not rewritten.
        _quiet.SetItems(QuietLabels(state.Quiet), QuietIndexOf(state.Quiet));

        // The kinds of toast are dimmed while the toast switch is off: they have no effect then (toasts by event).
        foreach (var (category, box) in _toastEvents)
        {
            box.IsChecked = state.ToastEvents.GetValueOrDefault(category, true);
            box.IsEnabled = state.Toast;
            box.Opacity = state.Toast ? 1 : 0.5;
        }
    }

    /// <summary>Takes the rail's palette: the text and the notes follow the theme. The pills and switches take the Ui brushes.</summary>
    public void ApplyTheme(RailPalette palette)
    {
        Foreground = RailPalette.Brush(palette.Text);
        var muted = RailPalette.Brush(palette.Muted);
        foreach (var note in _notes) note.Foreground = muted;
    }

    private static Thickness PageMargin => new(12, 0, 12, 12);

    private StackPanel Page(params UIElement[] children)
    {
        var page = new StackPanel { Margin = PageMargin, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var child in children) page.Children.Add(child);
        return page;
    }

    private TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 16, 0, 4),
    };

    private TextBlock Note(string text)
    {
        var note = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 8),
        };
        _notes.Add(note);
        return note;
    }

    /// <summary>A bold line that leads a group of switches, such as "Toasts for:".</summary>
    private static TextBlock Lead(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 10, 0, 2),
    };

    /// <summary>
    /// The quiet-hours pills: Off, the presets, and then the window in the file when it is not a preset (as "Custom (21:30-06:30)"). The labels
    /// are the same for every call with the same window, so the row is rebuilt only when they change.
    /// </summary>
    private static List<string> QuietLabels(QuietWindow? window)
    {
        var labels = new List<string> { "Off" };
        labels.AddRange(QuietHours.Presets.Select(QuietHours.Label));
        if (window is { } current && !QuietHours.Presets.Contains(current)) labels.Add($"Custom ({QuietHours.Label(current)})");
        return labels;
    }

    /// <summary>The pill chosen for the quiet hours: Off is 0, a preset is its place after Off, and a custom window is the last pill.</summary>
    private static int QuietIndexOf(QuietWindow? window)
    {
        if (window is null) return 0;
        var preset = QuietHours.Presets.ToList().IndexOf(window);
        return preset >= 0 ? preset + 1 : QuietHours.Presets.Count + 1;
    }

    /// <summary>The name of a kind of toast, as its switch shows it: what triggers it, in a few words.</summary>
    private static string EventLabel(ToastCategory category) => category switch
    {
        ToastCategory.Blocked => "Blocked: a session waits for a permission or a question",
        ToastCategory.Problems => "Problems: a restart loop, a failed start, a stopped or a failed session",
        ToastCategory.Notices => "Notices: a shared file, a conflict with the default branch, the run limit",
        ToastCategory.Ready => "Ready for review: a coder finished with changes",
        ToastCategory.LimitReset => "Usage limit reset",
        _ => category.ToString(),
    };

    private CheckBox Switch(string label) => new()
    {
        Style = (Style)FindResource("SwitchToggle"),
        Content = label,
        Margin = new Thickness(0, 4, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private static Button ActionButton(string text, Action click)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        button.Click += (_, _) => click();
        return button;
    }
}
