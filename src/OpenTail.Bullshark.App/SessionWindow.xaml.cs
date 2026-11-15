using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Cache;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Folders;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Trust;
using Wpf.Ui.Terminal;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The OpenTail.Bullshark window (S3, S5): a rail on the left, a pane on the right. The rail lists sessions grouped by folder;
/// the pane shows the chosen session's live terminal, a past session's transcript, or the folder's setup.
/// Each session has an embedded terminal attached to its background session with `claude attach`. Closing the window
/// detaches every terminal; the sessions keep running. The supervisor runs in this process so they stay alive.
/// The sessions come from the runs in runs.json (S5). A run whose folder is untrusted waits for trust; the other runs go on.
/// </summary>
public partial class SessionWindow : Window
{
    private readonly LaunchOptions _options;
    private readonly ClaudeCli _cli = new(new ProcessCliRunner());
    private readonly StateStore _store;
    private readonly RunStore _runStore;
    private readonly SupervisorLoop _loop;
    private readonly Dictionary<string, ExtendedTerminalControl> _terminals = new();

    /// <summary>The transcript file of each session, once found. A session with no file yet is looked for again.</summary>
    private readonly Dictionary<string, string> _transcriptPaths = new(StringComparer.Ordinal);

    /// <summary>
    /// Reads the cache facts of the running sessions. Runs off the UI thread. A transcript path is kept once found; a
    /// session whose file is not there yet is not kept, so it is looked for again on the next pass.
    /// </summary>
    private static (Dictionary<string, TailFacts> Facts, Dictionary<string, string> NewPaths) ReadRunningFacts(
        IEnumerable<string> sessionIds,
        Dictionary<string, string> knownPaths)
    {
        var facts = new Dictionary<string, TailFacts>(StringComparer.Ordinal);
        var newPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in sessionIds)
        {
            if (!knownPaths.TryGetValue(id, out var path))
            {
                path = TranscriptReader.FindTranscript(TranscriptReader.DefaultProjectsRoot, id);
                if (path is null) continue;
                newPaths[id] = path;
            }
            try
            {
                var tail = TranscriptTail.Read(path);
                if (tail is not null) facts[id] = tail;
            }
            catch (IOException ex)
            {
                AppLog.Write($"transcript tail not read: {ex.Message}");
            }
        }
        return (facts, newPaths);
    }
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly TranscriptView _transcript = new() { Visibility = Visibility.Hidden };
    private readonly SetupView _setup;
    private bool _passRunning;
    private bool _migrated;
    private IReadOnlyList<AgentInfo> _agents = [];

    /// <summary>The Trust steps that are open, one per untrusted repository root. The key is the folder key of the root.</summary>
    private readonly Dictionary<string, TrustStep> _trustSteps = new(StringComparer.OrdinalIgnoreCase);
    private sealed record TrustStep(string Root, ExtendedTerminalControl Terminal);
    private readonly DispatcherTimer _trustTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    /// <summary>The terminal the pane is showing, or null when a transcript or a setup page is showing.</summary>
    private ExtendedTerminalControl? _shown;

    public SessionWindow(LaunchOptions options)
    {
        _options = options;
        InitializeComponent();
        InitRail();
        _store = new StateStore(options.StatePath, AppLog.Write);
        _runStore = new RunStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(options.StatePath))!, "runs.json"));
        var runner = new SupervisorRunner(_cli, _store, TimeProvider.System, log: AppLog.Write);
        _loop = new SupervisorLoop(runner, _cli, _store, AppLog.Write);

        _setup = new SetupView(options.StatePath, options.Task) { Visibility = Visibility.Hidden };
        _setup.ShowFolder(options.WorkDir);
        _setup.ApplyTheme(Palette);
        _setup.RunRequested += OnRunRequested;
        _setup.TrustNeeded += root => ShowTrustStep(root, select: true);
        Pane.Children.Add(_transcript);
        Pane.Children.Add(_setup);

        Loaded += async (_, _) =>
        {
            // The history is read in the background at start, so the live sessions show at once.
            _ = LoadHistoryAsync();
            await PassAndRefreshAsync();
            StartSelfTestOrScreenshot();
        };
        _timer.Tick += async (_, _) => await PassAndRefreshAsync();
        _timer.Start();
        Closing += (_, _) => DetachAll();
        // The embedded terminal does not handle paste itself, so the window does: Ctrl+V and Shift+Insert.
        PreviewKeyDown += OnPanePreviewKeyDown;
        // While a Trust step is open, check again every few seconds, so the runs start as soon as trust is recorded.
        _trustTimer.Tick += async (_, _) => await PassAndRefreshAsync();
    }

    /// <summary>
    /// Every session of every run in runs.json. Reading only: the run file is written when a run starts, and once at the
    /// first pass when the default pair is migrated.
    /// </summary>
    private IReadOnlyList<SessionSpec> Specs => RunStore.SpecsOf(_runStore.Load());

    /// <summary>
    /// Runs one supervisor pass off the UI thread, then refreshes the rail. Untrusted folders get a Trust step and their
    /// runs are skipped; every other run is supervised. With --no-pass, nothing is started.
    /// </summary>
    private async Task PassAndRefreshAsync()
    {
        if (_passRunning) return;
        _passRunning = true;
        try
        {
            _agents = await Task.Run(() => _cli.ListAgentsAsync());

            // The cache facts of the running sessions are read here, off the UI thread, on every pass: a warm session turns cold
            // with no new record, so the labels are recomputed each time.
            var ids = _store.Load().Values.Select(r => r.SessionId).Where(id => id.Length > 0).ToList();
            var (facts, paths) = await Task.Run(() => ReadRunningFacts(ids,
                new Dictionary<string, string>(_transcriptPaths, StringComparer.Ordinal)));
            _runningFacts = facts;
            foreach (var pair in paths) _transcriptPaths[pair.Key] = pair.Value;

            // The default pair from before runs.json becomes the legacy run, once, and only if it exists (D-S13, D-S17).
            if (!_migrated)
            {
                _migrated = true;
                var legacy = _runStore.MigrateLegacyIfNeeded(_store.Load(), _agents, _options.WorkDir, _options.Task, DateTimeOffset.UtcNow);
                if (legacy is not null) AppLog.Write($"legacy run created for {legacy.WorkingDirectory}");
            }

            // Read the specs once, on the UI thread, before the pass runs on a pool thread.
            var specs = Specs;
            var (runnable, untrusted) = TrustGate.Filter(specs, TrustCheck.DefaultClaudeJsonPath);
            var needed = untrusted.Select(FolderKey.From).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var root in untrusted) ShowTrustStep(root, select: false);
            foreach (var key in _trustSteps.Keys.Where(k => !needed.Contains(k)).ToList()) HideTrustStep(key);
            var waiting = specs.Count(s => needed.Contains(FolderKey.From(s.WorkingDirectory)));
            TrustButton.Tag = _trustSteps.Values.FirstOrDefault()?.Root;
            TrustButton.Visibility = _trustSteps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            SetStatus("checking sessions…");
            var actions = _options.NoPass ? 0 : await Task.Run(() => _loop.RunPassAsync(runnable));
            RefreshRail();

            var failed = _store.Load().Values.Count(r => string.IsNullOrEmpty(r.SessionId));
            RetryButton.Visibility = failed > 0 ? Visibility.Visible : Visibility.Collapsed;
            var parts = new List<string> { $"{DateTime.Now:HH:mm:ss}  {actions} action(s) this pass" };
            if (waiting > 0) parts.Add($"{waiting} session(s) wait for trust (see the Trust step)");
            if (failed > 0) parts.Add($"{failed} start(s) failed: see the rail, then Retry failed starts");
            parts.Add("closing the window detaches the terminals; the sessions keep running");
            SetStatus(string.Join("  ·  ", parts));
        }
        catch (Exception ex)
        {
            AppLog.Write($"pass failed: {ex.Message}");
            SetStatus($"not running: {ex.Message}");
        }
        finally
        {
            _passRunning = false;
        }
    }

    /// <summary>
    /// Makes sure a terminal exists for a recorded session that has one. Terminals are created once, added to the pane
    /// once, and never moved between parents. Which one is visible is decided by <see cref="ShowOnly"/>.
    /// </summary>
    private ExtendedTerminalControl EnsureTerminal(SessionRecord record)
    {
        if (_terminals.TryGetValue(record.SessionId, out var existing)) return existing;

        var terminal = new ExtendedTerminalControl
        {
            StartupCommandLine = $"claude attach {ClaudeCli.ShortIdOf(record.SessionId)}",
            WorkingDirectory = _options.WorkDir,
            Visibility = Visibility.Hidden,
        };
        _terminals[record.SessionId] = terminal;
        Pane.Children.Add(terminal);
        return terminal;
    }

    /// <summary>
    /// Shows exactly one pane item. Every other terminal is Hidden, not Collapsed: a collapsed control may lay out at zero
    /// size, which can resize the pseudo-console to 0×0 or stop it (F45). Hidden keeps its size.
    /// </summary>
    private void ShowOnly(UIElement? element)
    {
        foreach (var terminal in AllTerminals())
            terminal.Visibility = ReferenceEquals(terminal, element) ? Visibility.Visible : Visibility.Hidden;
        _transcript.Visibility = ReferenceEquals(_transcript, element) ? Visibility.Visible : Visibility.Hidden;
        _setup.Visibility = ReferenceEquals(_setup, element) ? Visibility.Visible : Visibility.Hidden;

        _shown = element as ExtendedTerminalControl;
        if (_shown is not null)
        {
            var focus = _shown;
            Dispatcher.BeginInvoke(new Action(() => focus.Focus()), DispatcherPriority.Input);
        }
    }

    private IEnumerable<ExtendedTerminalControl> AllTerminals()
    {
        foreach (var terminal in _terminals.Values) yield return terminal;
        foreach (var step in _trustSteps.Values) yield return step.Terminal;
        if (_selfTestTerminal is not null) yield return _selfTestTerminal;
    }

    /// <summary>Detaches every terminal before the window closes. A background session keeps running after that.</summary>
    private void DetachAll()
    {
        foreach (var terminal in AllTerminals())
        {
            try
            {
                var term = terminal.DisconnectConPTYTerm();
                term?.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Write($"detach failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private void SetStatus(string text) => StatusText.Text = text;

    /// <summary>
    /// Opens the Trust step for an untrusted repository root: an interactive `claude` in that root, in the pane. The owner
    /// answers the prompt there. One step per root.
    /// </summary>
    private void ShowTrustStep(string root, bool select)
    {
        var key = FolderKey.From(root);
        if (!_trustSteps.TryGetValue(key, out var step))
        {
            var terminal = new ExtendedTerminalControl
            {
                StartupCommandLine = "claude",
                WorkingDirectory = root,
                Visibility = Visibility.Hidden,
            };
            Pane.Children.Add(terminal);
            step = new TrustStep(root, terminal);
            _trustSteps[key] = step;
            _trustTimer.Start();
            select = true;
        }

        if (select)
        {
            _selected = new RailTag(RailKind.Trust, key);
            ShowOnly(step.Terminal);
        }
    }

    /// <summary>Removes a Trust step and ends its Claude. Trust is already recorded when this is called.</summary>
    private void HideTrustStep(string key)
    {
        if (!_trustSteps.Remove(key, out var step)) return;
        try
        {
            step.Terminal.DisconnectConPTYTerm()?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Write($"trust step close failed: {ex.GetType().Name}: {ex.Message}");
        }
        Pane.Children.Remove(step.Terminal);
        if (ReferenceEquals(_shown, step.Terminal)) ShowOnly(_setup);
        if (_selected is { Kind: RailKind.Trust } selected && selected.Key == key) _selected = null;
        if (_trustSteps.Count == 0) _trustTimer.Stop();
    }

    /// <summary>
    /// Pastes clipboard text into the shown terminal as a bracketed paste, so Claude reads it as one paste and does not
    /// submit each line. Other keys are left alone.
    /// </summary>
    private void OnPanePreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var modifiers = System.Windows.Input.Keyboard.Modifiers;
        bool paste = (e.Key == System.Windows.Input.Key.V && modifiers == System.Windows.Input.ModifierKeys.Control)
                  || (e.Key == System.Windows.Input.Key.Insert && modifiers == System.Windows.Input.ModifierKeys.Shift);
        if (!paste || !System.Windows.Clipboard.ContainsText()) return;

        var term = _shown?.ConPTYTerm;
        if (term is null) return;

        var text = System.Windows.Clipboard.GetText().Replace("\r\n", "\r").Replace('\n', '\r');
        try
        {
            term.WriteToTerm("\u001b[200~" + text + "\u001b[201~");
        }
        catch (Exception ex)
        {
            AppLog.Write($"paste failed: {ex.GetType().Name}: {ex.Message}");
        }
        e.Handled = true;
    }

    /// <summary>Restarts the first open Trust step, for example after choosing 'No, exit' by mistake.</summary>
    private void TrustButton_Click(object sender, RoutedEventArgs e)
    {
        if (TrustButton.Tag is not string root) return;
        var key = FolderKey.From(root);
        HideTrustStep(key);
        ShowTrustStep(root, select: true);
    }

    /// <summary>
    /// Starts a run from the setup page: the run is written to runs.json, and the next pass starts its sessions. Its id
    /// and names avoid every name already in use, in the state file, in runs.json and in the agent listing (F33).
    /// </summary>
    private async void OnRunRequested(SetupRequest request)
    {
        var taken = _store.Load().Keys
            .Concat(RunStore.SpecsOf(_runStore.Load()).Select(s => s.Name))
            .Concat(_agents.Select(a => a.Name))
            .ToList();
        var runs = _runStore.Load().ToList();
        var run = SetupPlan.CreateRun(request, taken, DateTimeOffset.UtcNow);
        runs.Add(run);
        _runStore.Save(runs);
        AppLog.Write($"run {run.RunId} written: {run.Specs.Count} session(s) in {run.WorkingDirectory}");
        _setup.ShowStarted($"Run {run.RunId} written: {run.Specs.Count} session(s). They start on the next pass, within about 15 seconds.");
        await PassAndRefreshAsync();
    }

    /// <summary>Forgets the starts that failed, so the next pass tries them again. Only the owner asks for this.</summary>
    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        var kept = _store.Load().Where(pair => !string.IsNullOrEmpty(pair.Value.SessionId))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        _store.Save(kept);
        AppLog.Write("failed starts forgotten at the owner's request; the next pass tries them again");
        await PassAndRefreshAsync();
    }
}

/// <summary>Command-line options for the OpenTail.Bullshark window.</summary>
public sealed record LaunchOptions(
    string WorkDir,
    string Task,
    string StatePath,
    bool NoPass = false,
    string? ShotPath = null,
    double ShotAfterSeconds = 8,
    string? SelfTestPath = null,
    string? FilterName = null)
{
    public static LaunchOptions Parse(string[] args)
    {
        string? workdir = null, task = null, state = null, shot = null, selfTest = null, filterName = null;
        double shotAfter = 8;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--workdir": workdir = args[++i]; break;
                case "--task": task = args[++i]; break;
                case "--state": state = args[++i]; break;
                case "--shot": shot = args[++i]; break;
                case "--shot-after": shotAfter = double.Parse(args[++i], inv); break;
                case "--selftest": selfTest = args[++i]; break;
                case "--rail-filter": filterName = args[++i]; break;
            }
        }
        var defaultState = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenTail.Bullshark", "state.json");
        return new LaunchOptions(
            Path.GetFullPath(workdir ?? Environment.CurrentDirectory),
            task ?? "Read the project and ask for nothing: wait for the owner's first task.",
            state ?? defaultState,
            NoPass: args.Contains("--no-pass"),
            ShotPath: shot,
            ShotAfterSeconds: shotAfter,
            SelfTestPath: selfTest,
            FilterName: filterName);
    }
}
