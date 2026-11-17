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
    private IReadOnlyList<AgentInfo> _agents = [];

    /// <summary>
    /// The listing with completed sessions (`--all`) from the last pass. Names already in use come from this one, so a new run
    /// never reuses the name of a finished session: a `done` session of a run's name can then only be that run's own (F61).
    /// </summary>
    private IReadOnlyList<AgentInfo> _allAgents = [];

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
        InitLibrary(options.StatePath);
        var runner = new SupervisorRunner(_cli, _store, TimeProvider.System, log: AppLog.Write);
        _loop = new SupervisorLoop(runner, _cli, _store, AppLog.Write);
        _runner = runner;
        // The timeline's log sits beside the state file, and the supervisor writes every action to it (additions item 5).
        _events = new EventLog(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(options.StatePath))!, "events.jsonl"));
        runner.Events = _events;

        _setup = new SetupView(options.StatePath, options.Task) { Visibility = Visibility.Hidden };
        _setup.ShowFolder(options.WorkDir);
        if (options.SetupDetail is not null) _setup.ShowDetail(options.SetupDetail);
        _setup.ApplyTheme(Palette);
        _setup.RunRequested += OnRunRequested;
        _setup.TrustNeeded += root => ShowTrustStep(root, select: true);
        Pane.Children.Add(_transcript);
        _transcript.ResumeRequested += digest => _ = ResumeAsync(digest);
        Pane.Children.Add(_setup);
        Pane.Children.Add(_search);
        InitSearch();
        InitChanges();
        InitStorage();
        InitTimeline();
        InitTabDrag();
        InitZoom();

        Loaded += async (_, _) =>
        {
            // The history is read in the background at start, so the live sessions show at once. A --open-transcript run waits
            // for it, so that the transcript can be opened (TA4).
            if (_options.OpenSessionId is null) _ = LoadHistoryAsync();
            else await LoadHistoryAsync();
            await PassAndRefreshAsync();
            if (_options.OpenSessionId is { } openId) await OpenHistorySessionAsync(openId);
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
    private IReadOnlyList<SessionSpec> Specs => RunStore.SpecsOf(ReadRuns());

    /// <summary>
    /// The sessions' records as they are on disk. A file that cannot be read shows as nothing, and the status line says so.
    /// Nothing is written from what is shown: every write reads the file again under the lock, and refuses a file it cannot read.
    /// </summary>
    private IReadOnlyDictionary<string, SessionRecord> ReadRecords()
    {
        var (records, problem) = LoadRecords(_store);
        if (problem is not null) _storeProblem = problem;
        return records;
    }

    /// <summary>The runs as they are on disk. An unreadable file shows as no runs, as ReadRecords does.</summary>
    private IReadOnlyList<RunRecord> ReadRuns()
    {
        var (runs, problem) = LoadRuns(_runStore);
        if (problem is not null) _storeProblem = problem;
        return runs;
    }

    /// <summary>
    /// The records as they are on disk, or nothing and the reason when the file cannot be read. It touches no window object,
    /// so it runs on a pool thread as well as on the UI thread.
    /// </summary>
    private static (IReadOnlyDictionary<string, SessionRecord> Records, string? Problem) LoadRecords(StateStore store)
    {
        try
        {
            return (store.Load(), null);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write(ex.Message);
            return (new Dictionary<string, SessionRecord>(StringComparer.Ordinal), ex.Message);
        }
    }

    /// <summary>The runs on disk, or nothing and the reason when the file cannot be read. Safe on any thread, as LoadRecords is.</summary>
    private static (IReadOnlyList<RunRecord> Runs, string? Problem) LoadRuns(RunStore runStore)
    {
        try
        {
            return (runStore.Load(), null);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write(ex.Message);
            return (Array.Empty<RunRecord>(), ex.Message);
        }
    }

    /// <summary>The last store problem seen by a read in the current pass, or null.</summary>
    private string? _storeProblem;

    /// <summary>
    /// What a pass reads before it runs, gathered on a pool thread. The window copies it into its fields on the UI thread.
    /// </summary>
    private sealed record PassReads(
        IReadOnlyList<AgentInfo> Agents,
        IReadOnlyList<AgentInfo> PassAgents,
        IReadOnlyDictionary<string, SessionRecord> Records,
        TrustPlan Trust,
        Dictionary<string, TailFacts> Facts,
        Dictionary<string, string> NewPaths,
        string? StoreProblem);

    /// <summary>
    /// Runs one supervisor pass, then refreshes the rail. The reads and the pass itself run on a pool thread, so the window does
    /// not freeze while a folder lock is waited for or a `claude` command runs. Only the drawing happens on the UI thread, after
    /// the awaits. Untrusted folders get a Trust step and their runs are skipped; every other run is supervised. With --no-pass,
    /// or while Claude's trust record cannot be read, nothing is started.
    /// </summary>
    private async Task PassAndRefreshAsync()
    {
        if (_passRunning) return;
        _passRunning = true;
        try
        {
            var known = new Dictionary<string, string>(_transcriptPaths, StringComparer.Ordinal);
            var read = await Task.Run(() => ReadForPassAsync(known));

            _agents = read.Agents;
            _allAgents = read.PassAgents;
            _runningFacts = read.Facts;
            foreach (var pair in read.NewPaths) _transcriptPaths[pair.Key] = pair.Value;
            _storeProblem = read.StoreProblem;

            // While ~/.claude.json cannot be read, trust is unknown: no root is judged untrusted, and no run starts or restarts.
            var plan = read.Trust;
            var needed = plan.UntrustedRoots.Select(FolderKey.From).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var root in plan.UntrustedRoots) ShowTrustStep(root, select: false);
            foreach (var key in _trustSteps.Keys.Where(k => !needed.Contains(k)).ToList()) HideTrustStep(key);
            TrustButton.Tag = _trustSteps.Values.FirstOrDefault()?.Root;
            TrustButton.Visibility = _trustSteps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (_searchMatches is null && SearchBusy.Visibility != Visibility.Visible) SetStatus("checking sessions…");

            var actions = 0;
            var records = read.Records;
            if (!_options.NoPass && plan.Problem is null)
            {
                // The records the pass leaves are read on the pool thread too, so the rail is drawn from that one read.
                (actions, records, var afterProblem) = await Task.Run(() => RunPassAndReadAsync(read));
                _storeProblem = afterProblem ?? _storeProblem;
            }
            // The files two sessions of a run both wrote are read off the UI thread, from the transcripts this pass has seen (gems item 1).
            var paths = new Dictionary<string, string>(_transcriptPaths, StringComparer.Ordinal);
            var (clashes, budgets) = await Task.Run(() => ReadClashes(records, paths));
            _clashes = clashes;
            _runner.Budgets = budgets;
            // A coder that has just entered done gets its merge check, on a pool thread; its result shows on a later pass (gems item 2).
            UpdateMergeChecks(records);
            RefreshRail(records);
            AnnounceAttention();

            var failed = PassSnapshot.FailedStarts(records.Values);
            RetryButton.Visibility = failed > 0 ? Visibility.Visible : Visibility.Collapsed;
            var parts = PassSnapshot.StatusParts(DateTime.Now, actions, plan.Waiting, failed, _storeProblem, plan.Problem);

            // A search owns the status line while it runs or while its results are shown: the pass's summary waits for it to end.
            if (_searchMatches is null && SearchBusy.Visibility != Visibility.Visible) SetStatus(string.Join("  ·  ", parts));
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
    /// The reads a pass needs, on a pool thread. The agent listings come first: if one fails, nothing else is read and no pass runs.
    /// The active listing is for the rail; the pass gets its own listing with the completed sessions, so that a session that
    /// finished or was stopped is not taken for a lost one. Touches no window object.
    /// </summary>
    private async Task<PassReads> ReadForPassAsync(Dictionary<string, string> known)
    {
        var agents = await _cli.ListAgentsAsync();
        var passAgents = await _cli.ListAgentsAsync(includeCompleted: true);
        var (records, recordsProblem) = LoadRecords(_store);
        var (runs, runsProblem) = LoadRuns(_runStore);
        // A run that waits for its start time has no specs here, so it is neither trusted, started nor adopted yet (gems item 5).
        var plan = PassSnapshot.Trust(RunStore.DueSpecsOf(runs, DateTimeOffset.UtcNow), ClaudeTrust.Read(TrustCheck.DefaultClaudeJsonPath));

        // The cache facts of the running sessions are read on every pass: a warm session turns cold with no new record, so the
        // labels are recomputed each time.
        var ids = records.Values.Select(r => r.SessionId).Where(id => id.Length > 0).ToList();
        var (facts, paths) = ReadRunningFacts(ids, known);
        return new PassReads(agents, passAgents, records, plan, facts, paths, recordsProblem ?? runsProblem);
    }

    /// <summary>
    /// The pass itself, then the records it left, on a pool thread. A failed start is recorded by the runner, and the window
    /// counts the failed starts from these records.
    /// </summary>
    private async Task<(int Actions, IReadOnlyDictionary<string, SessionRecord> Records, string? Problem)> RunPassAndReadAsync(PassReads read)
    {
        var actions = await _loop.RunPassAsync(read.Trust.Runnable, read.PassAgents);
        var (records, problem) = LoadRecords(_store);
        return (actions, records, problem);
    }

    /// <summary>
    /// Makes sure a terminal exists for a recorded session that has one. Terminals are created once and added to the left pane.
    /// A terminal moves only between the left and the right pane (T2, F55). Which one is visible is decided by <see cref="ShowOnly"/>.
    /// </summary>
    private ExtendedTerminalControl EnsureTerminal(SessionRecord record)
    {
        if (_terminals.TryGetValue(record.SessionId, out var existing)) return existing;

        var terminal = new ExtendedTerminalControl
        {
            // The split self-test runs a counter in place of each session, so it needs no Claude session.
            StartupCommandLine = SelfTestCommand ?? $"claude attach {ClaudeCli.ShortIdOf(record.SessionId)}",
            WorkingDirectory = _options.WorkDir,
            LogConPTYOutput = SelfTestCommand is not null,
            Visibility = Visibility.Hidden,
        };
        _terminals[record.SessionId] = terminal;
        Pane.Children.Add(terminal);
        ConfigureTerminal(terminal);
        return terminal;
    }

    /// <summary>
    /// Shows exactly one pane item. Every other terminal is Hidden, not Collapsed: a collapsed control may lay out at zero
    /// size, which can resize the pseudo-console to 0×0 or stop it (F45). Hidden keeps its size.
    /// </summary>
    private void ShowOnly(UIElement? element)
    {
        // A session of the right pane is shown there, and the left pane keeps what it shows (T2).
        if (element is ExtendedTerminalControl right && ReferenceEquals(right.Parent, PaneB))
        {
            ShowInRight(right);
            return;
        }

        foreach (var terminal in AllTerminals())
        {
            if (ReferenceEquals(terminal.Parent, PaneB)) continue;
            terminal.Visibility = ReferenceEquals(terminal, element) ? Visibility.Visible : Visibility.Hidden;
        }
        _transcript.Visibility = ReferenceEquals(_transcript, element) ? Visibility.Visible : Visibility.Hidden;
        _setup.Visibility = ReferenceEquals(_setup, element) ? Visibility.Visible : Visibility.Hidden;
        _search.Visibility = ReferenceEquals(_search, element) ? Visibility.Visible : Visibility.Hidden;
        _changes.Visibility = ReferenceEquals(_changes, element) ? Visibility.Visible : Visibility.Hidden;
        _storage.Visibility = ReferenceEquals(_storage, element) ? Visibility.Visible : Visibility.Hidden;
        _timeline.Visibility = ReferenceEquals(_timeline, element) ? Visibility.Visible : Visibility.Hidden;

        _shown = element as ExtendedTerminalControl;
        if (_shown is not null)
        {
            var focus = _shown;
            _focusTerminal = focus;
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
            ConfigureTerminal(terminal);
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

        // Ctrl+plus, Ctrl+minus and Ctrl+0 change the zoom of the transcripts (zoom.md). The window gets them even while a
        // terminal has the keyboard (checked 2026-11-16).
        var zoom = OpenTail.Bullshark.Core.Zoom.ZoomKeys.FromVirtualKey(System.Windows.Input.KeyInterop.VirtualKeyFromKey(e.Key),
            (modifiers & System.Windows.Input.ModifierKeys.Control) != 0 && (modifiers & System.Windows.Input.ModifierKeys.Alt) == 0);
        if (zoom != OpenTail.Bullshark.Core.Zoom.ZoomKey.None)
        {
            ZoomBy(zoom);
            e.Handled = true;
            return;
        }
        bool paste = (e.Key == System.Windows.Input.Key.V && modifiers == System.Windows.Input.ModifierKeys.Control)
                  || (e.Key == System.Windows.Input.Key.Insert && modifiers == System.Windows.Input.ModifierKeys.Shift);
        if (!paste || !System.Windows.Clipboard.ContainsText()) return;

        var term = PasteTarget()?.ConPTYTerm;
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
    private async void OnRunRequested(SetupRequest request) => await StartRunAsync(request);

    /// <summary>Writes a run from a request and starts its pass. Setup and resume both start runs this way.</summary>
    private async Task StartRunAsync(SetupRequest request)
    {
        var taken = ReadRecords().Keys
            .Concat(RunStore.SpecsOf(ReadRuns()).Select(s => s.Name))
            .Concat(_agents.Concat(_allAgents).Select(a => a.Name))
            .ToList();
        var run = SetupPlan.CreateRun(request, taken, DateTimeOffset.UtcNow);
        try
        {
            _runStore.Update(runs => runs.Append(run).ToList());
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"run not written: {ex.Message}");
            _setup.ShowStarted($"Not started: {ex.Message}");
            _setup.RunFinished();
            return;
        }
        AppLog.Write($"run {run.RunId} written: {run.Specs.Count} session(s) in {run.WorkingDirectory}");
        _setup.ShowStarted($"Run {run.RunId} written: {run.Specs.Count} session(s). They start on the next pass, within about 15 seconds.");
        try
        {
            await PassAndRefreshAsync();
        }
        finally
        {
            _setup.RunFinished();
        }
    }

    /// <summary>Forgets the starts that failed, so the next pass tries them again. Only the owner asks for this.</summary>
    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _store.Update(records =>
            {
                foreach (var name in records.Where(pair => string.IsNullOrEmpty(pair.Value.SessionId)).Select(pair => pair.Key).ToList())
                    records.Remove(name);
            });
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"failed starts not forgotten: {ex.Message}");
            SetStatus($"Not changed: {ex.Message}");
            return;
        }
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
    string? FilterName = null,
    string? SetupDetail = null,
    string? SelfTestMode = null,
    string? OpenSessionId = null)
{
    public static LaunchOptions Parse(string[] args)
    {
        string? workdir = null, task = null, state = null, shot = null, selfTest = null, filterName = null, selfTestMode = null, setupDetail = null, openSession = null;
        double shotAfter = 8;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (args.Length > 0 && args[^1] == "--state") throw new ArgumentException("--state needs the path of a state file.");
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
                case "--selftest-mode": selfTestMode = args[++i]; break;
                case "--rail-filter": filterName = args[++i]; break;
                case "--setup-detail": setupDetail = args[++i]; break;
                case "--open-transcript": openSession = args[++i]; break;
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
            FilterName: filterName,
            SetupDetail: setupDetail,
            SelfTestMode: selfTestMode,
            OpenSessionId: openSession);
    }
}
