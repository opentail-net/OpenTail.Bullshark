using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Supervisor;

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
/// </summary>
public sealed record SettingsState(RailTheme Theme, bool Notify, bool Toast, bool MagicAuto, EscalationChoice Escalation);

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
    private readonly CheckBox _notify;
    private readonly CheckBox _toast;
    private readonly CheckBox _magic;
    private readonly List<TextBlock> _notes = [];
    private readonly Dictionary<SettingsTab, UIElement> _pages = new();
    private SettingsTab _current = SettingsTab.General;

    public SettingsView()
    {
        _notify = Switch("Sound and flash for new waits");
        _toast = Switch("Toasts when the window is in the background");
        _magic = Switch("Automatic magic naming");

        _tabs.SetItems(Headers, (int)_current);
        _tabs.Chosen += index => Show((SettingsTab)index);
        _theme.SetItems(ThemeHeaders, 0);
        _theme.Chosen += index => ThemeChosen?.Invoke(index == 1 ? RailTheme.Light : RailTheme.Dark);
        _escalate.SetItems(EscalationChoices.Presets.Select(EscalationChoices.PresetLabel).ToList(), 2);
        _escalate.Chosen += index =>
        {
            if (index < EscalationChoices.Presets.Count) EscalateChosen?.Invoke(EscalationChoices.Presets[index]);
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
            Note("A silent Windows toast when the window is not in front. It needs the sound and flash to be on."));

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

    /// <summary>A re-alert interval chosen from the presets, in minutes (0 is Off).</summary>
    public event Action<int>? EscalateChosen;

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
