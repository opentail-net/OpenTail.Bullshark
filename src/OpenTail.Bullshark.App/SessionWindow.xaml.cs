using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Behaviour;
using OpenTail.Bullshark.Core.Cache;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Folders;
using OpenTail.Bullshark.Core.Health;
using OpenTail.Bullshark.Core.OpenCode;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Storage;
using OpenTail.Bullshark.Core.Trust;
using OpenTail.Bullshark.Core.Ui;
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

    /// <summary>
    /// The provider the rail shows (R-8, stage C4): Claude or Codex, chosen in the dropdown at the top of the rail and saved in ui.json. A tab records the
    /// provider that opened it (<see cref="OpenTab.Provider"/>), and keeps it when this changes.
    /// </summary>
    private IProvider _activeProvider;

    /// <summary>Claude, built on the window's own folders. Always present: Claude's tabs and records are read from it whatever the dropdown shows.</summary>
    private readonly ClaudeProvider _claude;

    /// <summary>Codex, made when it is first chosen (C4). Null until then, and on a build where Codex is not offered.</summary>
    private CodexProvider? _codex;

    /// <summary>Copilot, made when it is first chosen or saved (G2.1). Null until then.</summary>
    private CopilotProvider? _copilot;

    /// <summary>opencode, made when it is first chosen or saved (OC2). Null until then.</summary>
    private OpenCodeProvider? _openCode;

    /// <summary>The window's Claude provider: the history and digests the rail and the transcript tabs read (R-2).</summary>
    private ClaudeProvider ActiveClaude => _claude;

    /// <summary>
    /// The key of a Claude session id in the window's dictionaries (R-3). The live records, the Claude rail rows and the library's Claude view all
    /// hold Claude sessions, so this key is Claude's whatever the dropdown shows. A tab's key is <see cref="KeyOf(OpenTab)"/>.
    /// </summary>
    private static SessionKey KeyOf(string sessionId) => new(ProviderId.Claude, sessionId);

    /// <summary>The key of a tab's session in the window's dictionaries: the provider that opened the tab, and its key (stage C, the review's item 1).</summary>
    internal static SessionKey KeyOf(OpenTab tab) => new(tab.Provider, tab.Key);

    private readonly ClaudeCli _cli = new(new ProcessCliRunner());
    private readonly StateStore _store;
    private readonly RunStore _runStore;
    private readonly SupervisorLoop _loop;
    private readonly Dictionary<SessionKey, ExtendedTerminalControl> _terminals = new();
    /// <summary>Session ids that are not valid short ids, each logged once (F4).</summary>
    private readonly HashSet<string> _badSessionIds = new(StringComparer.Ordinal);

    /// <summary>The transcript file of each session, once found. A session with no file yet is looked for again.</summary>
    private readonly Dictionary<string, string> _transcriptPaths = new(StringComparer.Ordinal);

    /// <summary>
    /// The sessions whose transcript search found nothing, for <c>lookups.missMemorySeconds</c> (behaviour batch, R10). Only a miss is kept here;
    /// the window clears it when the pane's session changes. Thread-safe: the searches run on pool threads.
    /// </summary>
    private readonly LookupMemory _misses = new();

    /// <summary>
    /// Finds a session's transcript through the miss memory (R10). A live miss is not searched for again: null is returned, as for a search that
    /// finds nothing. A search that finds nothing is remembered for <paramref name="missMemory"/>; one that finds the file forgets any miss for it.
    /// Runs on any thread, and logs nothing.
    /// </summary>
    private static string? FindTranscriptRemembered(LookupMemory misses, string projectsRoot, string sessionId, TimeSpan missMemory, DateTimeOffset now)
    {
        if (misses.IsRemembered(sessionId, now, missMemory)) return null;
        var path = TranscriptReader.FindTranscript(projectsRoot, sessionId);
        if (path is null) misses.Remember(sessionId, now, missMemory);
        else misses.Forget(sessionId);
        return path;
    }

    /// <summary>
    /// Reads the cache facts of the running sessions. Runs off the UI thread. A transcript path is kept once found; a
    /// session whose file is not there yet is not kept, and its miss is remembered for <paramref name="missMemory"/> (R10), so it is looked for again
    /// once that has passed.
    /// </summary>
    private static (Dictionary<string, TailFacts> Facts, Dictionary<string, string> NewPaths) ReadRunningFacts(
        IEnumerable<string> sessionIds,
        Dictionary<string, string> knownPaths,
        string projectsRoot,
        LookupMemory misses,
        TimeSpan missMemory)
    {
        var facts = new Dictionary<string, TailFacts>(StringComparer.Ordinal);
        var newPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        foreach (var id in sessionIds)
        {
            // A failure is this session's alone (round 12 B1): before it, one unreadable transcript failed every pass.
            try
            {
                if (!knownPaths.TryGetValue(id, out var path))
                {
                    path = FindTranscriptRemembered(misses, projectsRoot, id, missMemory, now);
                    if (path is null) continue;
                    newPaths[id] = path;
                }
                var tail = TranscriptTail.Read(path);
                if (tail is not null) facts[id] = tail;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLog.Write($"transcript tail not read: {ex.GetType().Name}");
            }
        }
        return (facts, newPaths);
    }
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
    // The soak log's counters and timer are in SessionWindow.Soak.cs (perf batch Q1).
    private readonly TranscriptView _transcript = new() { Visibility = Visibility.Hidden };
    private readonly SetupView _setup;
    private bool _passRunning;
    /// <summary>Cancels the pass that is running when the window closes (F5). Null when no pass runs.</summary>
    private CancellationTokenSource? _passCancel;
    /// <summary>True once the window is closing: the supervisor is then released by the pass that is still running, if there is one (F5).</summary>
    private bool _windowClosing;
    /// <summary>The session closes in progress: no pass starts while one runs, so a closed session is not resumed (F6).</summary>
    private int _sessionsClosing;

    /// <summary>
    /// The supervisor name of this state (reliability batch R1), held by this window while it runs the supervisor pass. Null when another window
    /// holds it, or when this window was opened with --no-pass: then this window only watches (<see cref="_watching"/>).
    /// </summary>
    private SingleInstance? _supervisor;

    /// <summary>True while another window supervises this state: this window reads and draws, and runs no pass (reliability batch R1).</summary>
    private bool _watching;

    /// <summary>Why the supervisor name could not be taken (reliability review R2), or null when it was only held by another window.</summary>
    private string? _supervisorProblem;

    /// <summary>When the last pass started. A trust-triggered pass waits 15 seconds after it (reliability batch R3).</summary>
    private DateTimeOffset _lastPassStarted = DateTimeOffset.MinValue;

    /// <summary>The problem state of the supervisor pass (reliability batch R9): the Needs-you row, and the one Problems toast when it starts.</summary>
    private readonly PassHealth _passHealth = new();

    /// <summary>The records and runs as the last UI-thread read got them: a read that finds the state busy keeps them (reliability batch R7).</summary>
    private IReadOnlyDictionary<string, SessionRecord>? _lastRecords;
    private IReadOnlyList<RunRecord>? _lastRuns;

    /// <summary>The status line of a window that only watches (reliability batch R1).</summary>
    private const string WatchingStatus =
        "this window only watches: another OpenTail.Bullshark window supervises this state  ·  closing the window detaches the terminals; the sessions keep running";

    /// <summary>The Needs-you note of a window that only watches (reliability batch R1).</summary>
    internal const string WatchingNote = "Another OpenTail.Bullshark window supervises this state. This one only watches.";

    /// <summary>The shortest time between two passes started by the Trust timer (reliability batch R3).</summary>
    private static readonly TimeSpan PassEvery = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The hold on starts and resumes while Respawn all runs and for 20 seconds after it (round 8 item 3). The pass reads it on a pool thread.
    /// </summary>
    private readonly RespawnHold _respawnHold = new();
    private IReadOnlyList<AgentInfo> _agents = [];

    /// <summary>The background daemon as the last pass read it (round 7 item 1). Used to show a listed session as stale while it is not running.</summary>
    private DaemonStatus _daemon = DaemonStatus.Unknown();

    /// <summary>
    /// The Needs-you note about the daemon: set while it is not running and listed sessions still claim working or blocked (round 7 item 1,
    /// revised). Null otherwise. It shows, and blocks nothing.
    /// </summary>
    private string? _daemonNotice;

    /// <summary>The daemon's version against the installed one, when they differ (round 7 item 2). Null when they match or either is unknown.</summary>
    private VersionDrift? _drift;

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

    /// <summary>The element the left pane showed last (R10). Showing a different element clears the lookup misses.</summary>
    private UIElement? _paneElement;

    /// <summary>
    /// The provider a window starts on, from the saved choice (C4, G2.1, OC2). Codex starts only where this build offers it (C-D2); a saved Copilot or
    /// opencode starts its own rail; anything else is Claude, as before. Pure.
    /// </summary>
    internal static ProviderId StartingProviderOf(ProviderId saved, bool codexAvailable) =>
        saved == ProviderId.Codex && codexAvailable ? ProviderId.Codex
        : saved == ProviderId.Copilot ? ProviderId.Copilot
        : saved == ProviderId.OpenCode ? ProviderId.OpenCode
        : ProviderId.Claude;

    public SessionWindow(LaunchOptions options)
    {
        // A test process must never supervise: a window built by a test with the owner's default state once resumed the owner's real planner session
        // (2026-11-19). Under a test host the window only reads and draws, whatever options it was given.
        if (!options.NoPass && RunningUnderTest.Is) options = options with { NoPass = true };
        _options = options;
        _claude = new ClaudeProvider(new ProviderPaths(options.ClaudeHome ?? ClaudePaths.ConfigDir(), options.ProjectsRoot, options.ArchiveRoot));
        // The provider the saved choice names (C4). Codex is started only where this build offers it (C-D2): otherwise Claude, as before.
        _activeProvider = _claude;
        var startingProvider = StartingProviderOf(ProviderStore.Load(options.StatePath), CodexProvider.Available);
        if (startingProvider == ProviderId.Codex)
            _activeProvider = _codex = new CodexProvider();
        // Copilot is offered on both targets (G2.1): the saved choice starts its rail.
        else if (startingProvider == ProviderId.Copilot)
            _activeProvider = _copilot = new CopilotProvider();
        // A saved opencode starts its rail (OC2), so it never starts on Claude.
        else if (startingProvider == ProviderId.OpenCode)
            _activeProvider = _openCode = new OpenCodeProvider();
        // The version probes (R4). Their recheck window is read from the behaviour settings at each check, so they are made here (see _claudeVersion).
        _claudeVersion = ToolVersionProbe.ForClaude(new ProcessCliRunner(), () => _behaviour.Versions.RecheckWindow);
        _codexVersion = ToolVersionProbe.ForCodex(new ProcessCliRunner("cmd.exe"), () => _behaviour.Versions.RecheckWindow);
        InitializeComponent();
        // The Insights page hosts the four views that were tabs of their own (2026-11-18). It is made before InitRail, which draws the theme into it.
        _insights = new InsightsView(_away, _night, _limits, _timeline, _storage) { Visibility = Visibility.Hidden };
        InitRail();
        InitProvider();
        _store = new StateStore(options.StatePath, AppLog.Write);
        _runStore = new RunStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(options.StatePath))!, "runs.json"));
        // The one supervisor of this state (reliability batch R1). A window that cannot take the name only watches: it reads and draws, and runs no pass.
        if (!options.NoPass)
        {
            _supervisor = SingleInstance.TryAcquire(SingleInstance.NameFor(options.StatePath), out var problem);
            _watching = _supervisor is null;
            NoteSupervisorProblem(problem);
        }
        // G-D15: the Copilot scratch folders of earlier runs go here, once this instance holds the guard for the state file (their parent is beside it).
        // Before any Copilot summary call could start in this process; a window that only watches (or runs no pass) removes nothing.
        if (_supervisor is not null) SweepCopilotScratch(options.StatePath);
        InitLibrary(options.StatePath);
        LoadMagicAuto();
        var runner = new SupervisorRunner(_cli, _store, TimeProvider.System, log: AppLog.Write)
        {
            Hold = _respawnHold,
            // A pass starts or resumes a session only when the installed claude may launch (R4).
            LaunchRefusal = PassLaunchRefusalAsync,
        };
        _loop = new SupervisorLoop(runner, _cli, _store, AppLog.Write);
        _runner = runner;
        // The timeline's log sits beside the state file, and the supervisor writes every action to it (additions item 5).
        _events = new EventLog(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(options.StatePath))!, "events.jsonl"), log: AppLog.Write);
        runner.Events = _events;

        _setup = new SetupView(options.StatePath, options.Task, options.ProjectsRoot) { Visibility = Visibility.Hidden };
        _setup.ShowFolder(options.WorkDir);
        if (options.SetupDetail is not null) _setup.ShowDetail(options.SetupDetail);
        _setup.ApplyTheme(Palette);
        _setup.RunRequested += OnRunRequested;
        _setup.TrustNeeded += root => ShowTrustStep(root, select: true);
        Pane.Children.Add(_transcript);
        _transcript.ResumeRequested += digest => _ = ResumeAsync(digest);
        _transcript.ExpandEffort = options.EffortOpen;
        Pane.Children.Add(_setup);
        Pane.Children.Add(_search);
        InitSearch();
        InitCodexSearch();
        InitCopilotSearch();
        InitOpenCodeSearch();
        InitChanges();
        InitReview();
        InitStorage();
        InitTimeline();
        InitLimits();
        InitAway();
        InitInsights();
        InitNight();
        InitSettings();
        InitToasts();
        InitTabDrag();
        InitZoom();
        InitSoak();

        Loaded += async (_, _) =>
        {
            // The history is read in the background at start, so the live sessions show at once. A --open-transcript run waits
            // for it, so that the transcript can be opened (TA4).
            if (_options.OpenSessionId is null) _ = LoadHistoryAsync();
            else await LoadHistoryAsync();
            // The Codex history is read at start too when Codex is the window's provider (C4); Claude's is read as before.
            if (IsCodexActive) _ = LoadCodexHistoryAsync();
            // The Copilot history is read at start too when Copilot is the window's provider (G2.1).
            if (IsCopilotActive) _ = LoadCopilotHistoryAsync();
            // The opencode history is read at start too when opencode is the window's provider (OC2).
            if (IsOpenCodeActive) _ = LoadOpenCodeHistoryAsync();
            await PassAndRefreshAsync();
            if (_options.OpenSessionId is { } openId) await OpenHistorySessionAsync(openId);
            // --find (test only): the shown transcript opens with its find bar on the query, as a search result does (find and jump batch, T4).
            if (_options.FindText is { } findText) _transcript.OpenFind(findText);
            // --search (test only): the rail's search box starts a search with this text, so the results can be shown for the screenshots.
            if (_options.SearchText is { } searchText) SearchBox.Text = searchText;
            OpenTestScreen();
            StartSelfTestOrScreenshot();
        };
        _timer.Tick += async (_, _) =>
        {
            // The session list is refreshed on the same tick when index.refreshSeconds has passed (R9); the refresh runs in the background.
            MaybeRefreshHistory();
            await PassAndRefreshAsync();
        };
        _timer.Start();
        Closing += (_, _) =>
        {
            // Set before the terminals are detached: the Codex process ends that the detach causes are then expected (R6).
            _appShuttingDown = true;
            DetachAll();
            // A closed window starts no more passes. A pass that is running is cancelled, and it gives the supervisor lock back when it ends,
            // so no pass writes after another window has taken the lock (F5).
            _windowClosing = true;
            _timer.Stop();
            _soakTimer?.Stop();
            WriteSoakLine("close");
            _trustTimer.Stop();
            _passCancel?.Cancel();
            if (!_passRunning) ReleaseSupervisor();
        };
        // The embedded terminal does not handle paste itself, so the window does: Ctrl+V and Shift+Insert.
        PreviewKeyDown += OnPanePreviewKeyDown;
        // While a Trust step is open, check again every few seconds, so the runs start as soon as trust is recorded.
        _trustTimer.Tick += async (_, _) => await TrustStepTickAsync();
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
        try
        {
            // The UI thread waits briefly for the lock (reliability batch R7). A busy file gives the last read, and the status line says so once.
            var records = _store.Load(UiFile.UiWait);
            ReadBusy.Note(false);
            _lastRecords = records;
            return records;
        }
        catch (TimeoutException)
        {
            ReadBusy.Note(true);
            return _lastRecords ?? new Dictionary<string, SessionRecord>(StringComparer.Ordinal);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"session records not read: {ex.GetType().Name}");
            _storeProblem = ex.Message;
            return new Dictionary<string, SessionRecord>(StringComparer.Ordinal);
        }
    }

    /// <summary>The runs as they are on disk. An unreadable file shows as no runs, as ReadRecords does.</summary>
    private IReadOnlyList<RunRecord> ReadRuns()
    {
        try
        {
            var runs = _runStore.Load(UiFile.UiWait);
            ReadBusy.Note(false);
            _lastRuns = runs;
            return runs;
        }
        catch (TimeoutException)
        {
            ReadBusy.Note(true);
            return _lastRuns ?? Array.Empty<RunRecord>();
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"runs not read: {ex.GetType().Name}");
            _storeProblem = ex.Message;
            return Array.Empty<RunRecord>();
        }
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
            AppLog.Write($"session records not read: {ex.GetType().Name}");
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
            AppLog.Write($"runs not read: {ex.GetType().Name}");
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
        IReadOnlyDictionary<string, RunBudget> Budgets,
        string? StoreProblem,
        DaemonStatus Daemon,
        VersionDrift? Drift,
        string? DaemonNotice,
        string? DaemonStatusNotice,
        PassSettings? Settings,
        BehaviourLoad? Behaviour);

    /// <summary>
    /// The ui.json values the pass uses (perf and testability batch, Q2). Read once per pass on the pool thread and kept here for the pass's drawing and
    /// announcements; the UI thread does not read ui.json on the pass path. Kept at the last values read when the file stayed busy.
    /// </summary>
    private PassSettings _passSettings = PassSettings.Defaults;

    /// <summary>
    /// The behaviour settings (behaviour.json, decisions of 2026-11-22), read with each pass on the pool thread: the alerts, the re-alert interval and the
    /// focus gate read them. Kept at the last values read when the file stayed busy. UI thread only.
    /// </summary>
    private BehaviourSettings _behaviour = BehaviourSettings.Defaults;

    /// <summary>The last note logged about behaviour.json (a problem, or values not used), so the note is logged once and not on every pass.</summary>
    private string? _behaviourNote;

    /// <summary>
    /// Runs one supervisor pass, then refreshes the rail. The reads and the pass itself run on a pool thread, so the window does
    /// not freeze while a folder lock is waited for or a `claude` command runs. Only the drawing happens on the UI thread, after
    /// the awaits. Untrusted folders get a Trust step and their runs are skipped; every other run is supervised. With --no-pass,
    /// or while Claude's trust record cannot be read, nothing is started.
    /// </summary>
    private async Task PassAndRefreshAsync()
    {
        // The status-bar limit is drawn and, at most once a minute, scanned here too (A2). It does not wait for the pass.
        RefreshLimitStatus();
        if (_passRunning || _windowClosing || _sessionsClosing > 0) return;
        _passRunning = true;
        var pass = _passCancel = new CancellationTokenSource();
        var passToken = pass.Token;
        _lastPassStarted = DateTimeOffset.UtcNow;
        var passClock = System.Diagnostics.Stopwatch.StartNew();
        _passLaunchNote = null;
        // Nothing between the flag and the try may throw: a flag left set stops every later pass without a word (review R2).
        try
        {
            TakeOverIfFree();
            var known = new Dictionary<string, string>(_transcriptPaths, StringComparer.Ordinal);
            // The miss memory's window is read here, on the UI thread, from the behaviour settings in force (R10).
            var missMemory = _behaviour.Lookups.MissMemory;
            var read = await Task.Run(() => ReadForPassAsync(known, missMemory));

            _agents = read.Agents;
            _allAgents = read.PassAgents;
            _listingKnown = true;
            _runningFacts = read.Facts;
            if (read.Settings is { } settings) _passSettings = settings;
            if (read.Behaviour is { } behaviour) NoteBehaviour(behaviour);
            foreach (var pair in read.NewPaths) _transcriptPaths[pair.Key] = pair.Value;
            _storeProblem = read.StoreProblem;
            // The limits this pass is held to are set before it runs (round 6 item 1): a run at its limit gets no start or resume from the
            // first pass on. The runner is idle here, since a pass runs only when the previous one has finished.
            _runner.Budgets = read.Budgets;
            // The daemon as this pass read it (round 7 item 1): a dead daemon makes the pass decide nothing, and shows in the Needs you group.
            _daemon = read.Daemon;
            _daemonNotice = read.DaemonNotice;
            _runner.Daemon = read.Daemon;
            // A daemon that runs another claude version gets the Respawn all button, and only the owner's confirmed click runs anything (round 7 item 2).
            _drift = read.Drift;
            // A window that only watches does not offer the respawn: it restarts the sessions, which only the supervising window does.
            RespawnButton.Visibility = _drift is null || _watching || !ClaudeSupervision ? Visibility.Collapsed : Visibility.Visible;
            RespawnButton.ToolTip = _drift?.Message;

            // While ~/.claude.json cannot be read, trust is unknown: no root is judged untrusted, and no run starts or restarts.
            var plan = read.Trust;
            var needed = plan.UntrustedRoots.Select(FolderKey.From).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // A window that only watches opens no Trust step: an interactive claude there is the supervising window's to open.
            if (!_watching) foreach (var root in plan.UntrustedRoots) ShowTrustStep(root, select: false);
            foreach (var key in _trustSteps.Keys.Where(k => !needed.Contains(k)).ToList()) HideTrustStep(key);
            TrustButton.Tag = _trustSteps.Values.FirstOrDefault()?.Root;
            TrustButton.Visibility = _trustSteps.Count > 0 && ClaudeSupervision ? Visibility.Visible : Visibility.Collapsed;

            if (_searchMatches is null && SearchBusy.Visibility != Visibility.Visible) SetStatus("checking sessions…");

            var actions = 0;
            var records = read.Records;
            // True only when the supervisor really ran this pass: the heartbeat (night batch N1) says so and no other pass does.
            var passRan = false;
            string? afterProblem = null;
            if (_supervisor is not null && plan.Problem is null)
            {
                // The usage-limit holds are read BEFORE the decisions (review of 2026-11-21): the pass that first finds a session gone must already know its
                // limit stop, or it could resume the session once before the reset.
                await ProbeLimitsAsync(read.Records, DateTimeOffset.Now);
                _runner.LimitHolds = LimitHoldsOf(read.Records, _limitStops, DateTimeOffset.Now, _limitUnknown);
                // The records the pass leaves are read on the pool thread too, so the rail is drawn from that one read.
                (actions, records, afterProblem) = await Task.Run(() => RunPassAndReadAsync(read, passToken));
                _storeProblem = afterProblem ?? _storeProblem;
                _login = _loop.LastLogin;
                passRan = true;
            }
            // This window supervises: what its pass found is reported once (reliability batch R9). A window that only watches reports nothing.
            if (_supervisor is not null) ReportPass(plan.Problem ?? read.StoreProblem ?? afterProblem);
            // The files two sessions of a run both wrote are read off the UI thread, from the transcripts this pass has seen (gems item 1).
            var paths = new Dictionary<string, string>(_transcriptPaths, StringComparer.Ordinal);
            // The budgets read after the pass are kept for the next one, and the clashes are drawn from them (round 6 item 1). The runs read here
            // are the rail's and the merge checks' (round 12 B4): a runs.json that cannot be read shows its problem, as ReadRuns did.
            // The clock setting is read here, on the window's thread, and passed in: the read runs on the pool (R5).
            var clampBackwards = _behaviour.State.ClampClockBackwards;
            var (clashes, budgets, runs, runsProblem) = await Task.Run(() => ReadClashes(records, paths, read.Agents, clampBackwards));
            _clashes = clashes;
            _runner.Budgets = budgets;
            if (runsProblem is not null) _storeProblem = runsProblem;
            // A coder that has just entered done gets its merge check, on a pool thread; its result shows on a later pass (gems item 2).
            UpdateMergeChecks(records, RunStore.SpecsOf(runs));
            // The finished coders' transcripts are read here too, for the Ready for review lane (ready-for-review D1), off the UI thread.
            _reviewSources = await Task.Run(() => ReviewSourcesOf(records, paths, read.PassAgents));
            // The usage-limit stops of the supervised and the flagged sessions, read here so the rail and Needs you show them (limit wake).
            await ProbeLimitsAsync(records, DateTimeOffset.Now);
            // The usage-limit holds from this probe: the next pass does not resume a lost session before its limit resets (review item 1). Like the
            // budgets, they apply from the next pass on.
            _runner.LimitHolds = LimitHoldsOf(records, _limitStops, DateTimeOffset.Now, _limitUnknown);
            RefreshRail(records, runs, _passSettings);
            AnnounceAttention();
            // The wake after the limit resets: only here, once per pass, with the trust known (limit wake, 2026-11-20). A start redraws the rail.
            if (await WakeDueSessionsAsync(DateTimeOffset.Now, trustKnown: plan.Problem is null)) RefreshRail();
            WriteAttentionFile();
            if (passRan) WriteHeartbeat(DateTimeOffset.Now);

            var failed = PassSnapshot.FailedStarts(records.Values);
            RetryButton.Visibility = failed > 0 && ClaudeSupervision ? Visibility.Visible : Visibility.Collapsed;
            // Under Codex no Trust step is shown, so the "see the Trust step" count is replaced by the Codex text, which says how to answer (F-F).
            var parts = PassSnapshot.StatusParts(DateTime.Now, actions, ClaudeSupervision ? plan.Waiting : 0, failed, _storeProblem, plan.Problem, read.DaemonStatusNotice, _drift?.StatusText).ToList();
            if (!ClaudeSupervision && plan.UntrustedRoots.Count > 0) parts.Insert(parts.Count - 1, CodexTrustWait(plan.UntrustedRoots));
            // A start the version check refused, or a version that could not be read, is said first (R4).
            if (_passLaunchNote is { } launchNote) parts.Insert(0, launchNote);

            // A search owns the status line while it runs or while its results are shown: the pass's summary waits for it to end.
            if (_searchMatches is null && SearchBusy.Visibility != Visibility.Visible)
            {
                // A crash notice (firstrun batch B3) stays at the front of the line until the window closes.
                _crashNotice ??= CrashFiles.TakeNotice();
                var line = _watching
                    ? _supervisorProblem is null ? WatchingStatus : $"this window only watches: {_supervisorProblem}"
                    : string.Join("  ·  ", parts);
                SetStatus(_crashNotice is null ? line : $"{_crashNotice}  ·  {line}");
                if (ReadBusy.TakeNotice()) SetStatus(ReadBusy.Notice);
            }
        }
        catch (OperationCanceledException) when (_windowClosing)
        {
            // The window closed during the pass and cancelled it (F5): nothing is reported, and the lock is given back below.
        }
        catch (Exception ex)
        {
            // A pass that failed may not have read the session list, so the archive waits for the next pass that does (round 5 item 4).
            _listingKnown = false;
            // The owner reads a sentence with a next step, not the exception (firstrun batch B2); the log keeps the exception.
            AppLog.Write($"pass failed: {ex.GetType().Name}");
            var sentence = PassErrorText.Of(ex);
            SetStatus($"not running: {sentence}");
            if (_supervisor is not null) ReportPass(sentence);
        }
        finally
        {
            _passRunning = false;
            NotePassDone(passClock.Elapsed.TotalMilliseconds);
            pass.Dispose();
            _passCancel = null;
            // A window that closed while this pass ran gives the supervisor lock back now, after the pass has stopped (F5).
            if (_windowClosing) ReleaseSupervisor();
            _storage.SetArchiveAvailable(ArchiveAllowed);
            // Automatic magic naming (off by default) runs only once a pass has finished, so the window is idle (SessionWindow.MagicRename.cs).
            // Automatic naming calls claude, so only the supervising window runs it.
            if (!_watching && !_windowClosing) MaybeAutoMagicRename();
        }
    }

    /// <summary>
    /// Takes the supervisor name when this window does not hold it and it is free now (reliability batch R1). A window that started while another
    /// one supervised the state takes over when that one has closed, so nothing waits for a restart.
    /// </summary>
    private void TakeOverIfFree()
    {
        if (_supervisor is not null || _options.NoPass) return;
        var taken = SingleInstance.TryAcquire(SingleInstance.NameFor(_options.StatePath), out var problem);
        NoteSupervisorProblem(problem);
        if (taken is null) return;
        _supervisor = taken;
        _watching = false;
        AppLog.Write("this window supervises the state now: the window that did before has closed");
    }

    /// <summary>
    /// Keeps the reason the supervisor name could not be taken (reliability review R2). A window that only watches for that reason says so on its status
    /// line, and the log has the reason once, not on every pass. Null means the name was only held by another window, which the status line says.
    /// </summary>
    private void NoteSupervisorProblem(string? problem)
    {
        if (problem == _supervisorProblem) return;
        _supervisorProblem = problem;
        if (problem is not null) AppLog.Write($"supervisor: {problem}; this window only watches");
    }

    /// <summary>Gives the supervisor name back when the window closes, so another window can take it (reliability batch R1).</summary>
    private void ReleaseSupervisor()
    {
        // A clean close takes the attention file with it (firstrun batch B9): a stale file would still say that items wait.
        if (_supervisor is not null) DeleteAttentionFile();
        _supervisor?.Dispose();
        _supervisor = null;
    }

    /// <summary>
    /// What a supervising window's pass found (reliability batch R9). A failure that starts raises the Needs-you row and one Problems toast; one that
    /// goes on keeps the row only; a pass that works again clears it. <paramref name="problem"/> is null when the pass went well.
    /// </summary>
    private void ReportPass(string? problem)
    {
        switch (_passHealth.Observe(problem))
        {
            case PassHealthChange.Failed:
                AnnounceProblem(_passHealth.Problem!);
                break;
            case PassHealthChange.Recovered:
                AppLog.Write("supervisor problem cleared: the pass works again");
                break;
        }
    }

    /// <summary>
    /// The timer of the open Trust steps (reliability batch R3). Every 3 seconds it reads Claude's trust file on a pool thread and runs no claude.
    /// A full pass follows only when a root with a step is trusted now, and at least 15 seconds have passed since the last pass started. The
    /// 15-second timer runs a pass in any case.
    /// </summary>
    private async Task TrustStepTickAsync()
    {
        if (_passRunning || _trustSteps.Count == 0) return;
        var roots = _trustSteps.Values.Select(step => step.Root).ToList();
        var path = TrustCheck.DefaultClaudeJsonPath;
        var trust = await Task.Run(() => ClaudeTrust.Read(path));
        if (trust.Problem is not null || !roots.Any(trust.IsTrusted)) return;
        if (DateTimeOffset.UtcNow - _lastPassStarted < PassEvery) return;
        await PassAndRefreshAsync();
    }

    /// <summary>
    /// The reads a pass needs, on a pool thread. The daemon is read before the agent listings and again after them; if a listing fails, nothing else is read and no pass runs.
    /// The active listing is for the rail; the pass gets its own listing with the completed sessions, so that a session that
    /// finished or was stopped is not taken for a lost one. Touches no window object.
    /// </summary>
    private async Task<PassReads> ReadForPassAsync(Dictionary<string, string> known, TimeSpan missMemory)
    {
        // The daemon is read before the listings and again after them, and never throws: an unknown status lets the pass go on as before
        // (round 7 item 1). A listed row is stale only when both reads say not running (round 9 item 3), so a daemon that starts or exits
        // while the listings are read does not turn a live row into a stale one, or the reverse.
        var daemonBefore = await _cli.DaemonStatusAsync();
        var rawAgents = await _cli.ListAgentsAsync();
        var rawPassAgents = await _cli.ListAgentsAsync(includeCompleted: true);
        var daemonAfter = await _cli.DaemonStatusAsync();
        var daemon = DaemonStatus.Around(daemonBefore, daemonAfter);
        // While the daemon is not running (its normal idle state), a listed working or blocked session is shown and decided on as stale. The
        // Needs-you note and the status note count those sessions, and never block anything (round 7 item 1, revised by the planner).
        var agents = daemon.IdleListing(rawAgents);
        var passAgents = daemon.IdleListing(rawPassAgents);
        var daemonNotice = daemon.NeedsYouNotice(rawAgents);
        var daemonStatusNotice = daemon.StatusNotice(rawAgents);
        // The installed version is read only when the daemon reports one: there is no drift to show without both (round 7 item 2).
        var cliVersion = daemon.Version is null ? null : await _cli.VersionAsync();
        // Kept for the Settings line and the diagnostics (firstrun batch B5): shown only once a pass has read it; nothing starts claude for it.
        _cliVersion = cliVersion ?? _cliVersion;
        var drift = VersionDrift.OfStatus(daemon, cliVersion);
        var (records, recordsProblem) = LoadRecords(_store);
        var (runs, runsProblem) = LoadRuns(_runStore);
        // A run that waits for its start time has no specs here, so it is neither trusted, started nor adopted yet (gems item 5).
        var plan = PassSnapshot.Trust(RunStore.DueSpecsOf(runs, DateTimeOffset.UtcNow), ClaudeTrust.Read(TrustCheck.DefaultClaudeJsonPath));

        // The cache facts of the running sessions are read on every pass: a warm session turns cold with no new record, so the
        // labels are recomputed each time.
        var ids = records.Values.Select(r => r.SessionId).Where(id => id.Length > 0).ToList();
        var (facts, newPaths) = ReadRunningFacts(ids, known, _options.ProjectsRoot, _misses, missMemory);

        // The run limits are read here, before the pass, from the records and transcripts above (round 6 item 1). The transcript paths are
        // the known ones and the new ones. The cache is read under its lock (round 12 B3).
        var paths = new Dictionary<string, string>(known, StringComparer.Ordinal);
        foreach (var pair in newPaths) paths[pair.Key] = pair.Value;
        // The ui.json values the pass draws and announces with (perf and testability batch, Q2): one read, here on the pool thread.
        var uiReadable = UiFile.TryReadAllInBackground(_options.StatePath, out var uiRoot);
        var settings = uiReadable ? PassSettings.From(uiRoot) : null;
        // behaviour.json is read with ui.json (behaviour batch, R2). Its legacy re-alert value is ui.json's escalateMinutes, when the key is there.
        // A busy ui.json keeps the last behaviour values too, so the interval does not flicker to zero for one pass.
        var behaviour = uiReadable ? BehaviourStore.LoadBeside(_options.StatePath, uiRoot?[PassSettings.EscalateKey]) : null;
        // The budgets take the clock setting from the behaviour read just above (R5).
        var budgets = BudgetsOf(runs, records, paths, (behaviour?.Settings ?? BehaviourSettings.Defaults).State.ClampClockBackwards);
        return new PassReads(agents, passAgents, records, plan, facts, newPaths, budgets, recordsProblem ?? runsProblem, daemon, drift,
            daemonNotice, daemonStatusNotice, settings, behaviour);
    }

    /// <summary>
    /// The pass itself, then the records it left, on a pool thread. A failed start is recorded by the runner, and the window
    /// counts the failed starts from these records.
    /// </summary>
    private async Task<(int Actions, IReadOnlyDictionary<string, SessionRecord> Records, string? Problem)> RunPassAndReadAsync(
        PassReads read, CancellationToken cancellationToken)
    {
        var actions = await _loop.RunPassAsync(read.Trust.Runnable, read.PassAgents, cancellationToken);
        var (records, problem) = LoadRecords(_store);
        return (actions, records, problem);
    }

    /// <summary>
    /// Makes sure a terminal exists for a recorded session that has one. Terminals are created once and added to the left pane.
    /// A terminal moves only between the left and the right pane (T2, F55). Which one is visible is decided by <see cref="ShowOnly"/>.
    /// </summary>
    private ExtendedTerminalControl? EnsureTerminal(SessionRecord record)
    {
        if (_terminals.TryGetValue(KeyOf(record.SessionId), out var existing)) return existing;

        // The split self-test runs a counter in place of each session, so it needs no Claude session. Otherwise the id must be a valid short
        // id, or no terminal is made for the session (F4): the line is not built from a bad id, and the message is logged once per id.
        var attach = SelfTestCommand ?? ClaudeCli.AttachCommandLineOf(record.SessionId);
        if (attach is null)
        {
            if (_badSessionIds.Add(record.SessionId)) AppLog.Write("bad session id: no terminal is started for a session whose id is not 8 hex characters");
            return null;
        }

        var terminal = new ExtendedTerminalControl
        {
            StartupCommandLine = attach,
            WorkingDirectory = _options.WorkDir,
            LogConPTYOutput = SelfTestCommand is not null,
            Visibility = Visibility.Hidden,
        };
        _terminals[KeyOf(record.SessionId)] = terminal;
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
        // The Night page refreshes itself only while it is the page shown (night batch N6).
        if (!ReferenceEquals(_insights, element)) SetNightLive(false);
        // The Codex transcript tabs (parity P7): each is shown only when it is the element.
        foreach (var codexView in _codexTranscripts.Values)
        {
            codexView.Visibility = ReferenceEquals(codexView, element) ? Visibility.Visible : Visibility.Hidden;
        }
        _setup.Visibility = ReferenceEquals(_setup, element) ? Visibility.Visible : Visibility.Hidden;
        _search.Visibility = ReferenceEquals(_search, element) ? Visibility.Visible : Visibility.Hidden;
        // The Codex search results (parity P6): shown only when they are the element.
        _codexSearch.Visibility = ReferenceEquals(_codexSearch, element) ? Visibility.Visible : Visibility.Hidden;
        _changes.Visibility = ReferenceEquals(_changes, element) ? Visibility.Visible : Visibility.Hidden;
        // The Insights and Settings pages. The four views inside Insights are shown by it (InsightsView.Show), not here.
        _insights.Visibility = ReferenceEquals(_insights, element) ? Visibility.Visible : Visibility.Hidden;
        _settings.Visibility = ReferenceEquals(_settings, element) ? Visibility.Visible : Visibility.Hidden;

        // The pane shows another element (a different session, or a page): the lookup misses are forgotten (R10). Showing the same element keeps them.
        if (!ReferenceEquals(element, _paneElement)) _misses.Clear();
        _paneElement = element;
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
        CloseDetachedWindows();
        foreach (var terminal in AllTerminals()) DetachConnection(terminal);
    }

    private void SetStatus(string text) => StatusText.Text = text;

    /// <summary>
    /// Opens the Trust step for an untrusted repository root: an interactive `claude` in that root, in the pane. The owner
    /// answers the prompt there. One step per root. Under Codex no step is started and the selection does not move (F-F): the Claude
    /// run stays pending, and the status line says that the owner answers it by switching the provider to Claude. This is the only
    /// place a trust terminal is created, so every caller (the pass and the setup page) is covered.
    /// </summary>
    private void ShowTrustStep(string root, bool select)
    {
        if (!ClaudeSupervision)
        {
            SetStatus(CodexTrustWait([root]));
            return;
        }

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

    /// <summary>
    /// The status text for Claude runs that wait for trust while Codex is shown (F-F). No Trust step is started under Codex, so the text
    /// names the folder and says how to answer the prompt: by switching the provider to Claude.
    /// </summary>
    private static string CodexTrustWait(IReadOnlyList<string> roots)
    {
        var more = roots.Count > 1 ? $" and {roots.Count - 1} more" : string.Empty;
        return $"a Claude run waits for trust on {roots[0]}{more}; answer the trust prompt by switching the provider to Claude";
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
            AppLog.Write($"trust step close failed: {ex.GetType().Name}");
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

        // Ctrl+Shift+P opens the command palette (ideas D3). It is the only chord checked here for it, and the zoom and the paste below are
        // unchanged. The palette opens once this key is finished, so the new window takes the keyboard after the key's own handling.
        if (e.Key == System.Windows.Input.Key.P && modifiers == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift))
        {
            e.Handled = true;
            Dispatcher.BeginInvoke(new Action(OpenPalette), System.Windows.Threading.DispatcherPriority.Input);
            return;
        }

        // Ctrl+Shift+N goes to the session that waits next, after the active one (keys-and-find plan, step 6). It is handled here like Ctrl+Shift+P,
        // so a terminal with the keyboard does not get it. It only shows a tab (GoToSession): nothing is sent to a session.
        if (e.Key == System.Windows.Input.Key.N && modifiers == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift))
        {
            e.Handled = true;
            Dispatcher.BeginInvoke(new Action(GoToNextWaiting), System.Windows.Threading.DispatcherPriority.Input);
            return;
        }

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
        // Text on the clipboard is pasted by the window. An image alone is not: Claude Code takes it on Alt+V (ESC v), and a bare Ctrl+V byte
        // does nothing there, so the window sends the same Alt+V for the owner's Ctrl+V (PasteText.ImagePasteKeys).
        var hasText = paste && System.Windows.Clipboard.ContainsText();
        var hasImage = paste && !hasText && System.Windows.Clipboard.ContainsImage();
        if (!hasText && !hasImage) return;
        // A text box of this window (the Changes note, the rail's search) pastes into itself. Without this, PasteTarget can still be a session
        // of the right pane that is on screen, and the clipboard would go into that session (principle 3). A terminal with focus has no WPF
        // focused element, so the paste into a terminal is unchanged.
        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;

        var term = PasteTarget()?.ConPTYTerm;
        if (term is null) return;

        if (hasImage)
        {
            try
            {
                term.WriteToTerm(OpenTail.Bullshark.Core.Terminal.PasteText.ImagePasteKeys);
            }
            catch (Exception ex)
            {
                AppLog.Write($"image paste failed: {ex.GetType().Name}");
            }
            e.Handled = true;
            return;
        }

        // ESC and the other control characters go, so the clipboard cannot end the bracketed paste early (PasteText, F1).
        var text = OpenTail.Bullshark.Core.Terminal.PasteText.Clean(System.Windows.Clipboard.GetText());
        try
        {
            term.WriteToTerm("\u001b[200~" + text + "\u001b[201~");
        }
        catch (Exception ex)
        {
            AppLog.Write($"paste failed: {ex.GetType().Name}");
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

    /// <summary>
    /// Writes a run from a request and starts its pass. Setup and resume both start runs this way. The version check comes first, so a refused launch
    /// writes no run (R4). Returns the check, which may carry a warning, when the run was written; null when nothing was written.
    /// </summary>
    private async Task<LaunchCheck?> StartRunAsync(SetupRequest request)
    {
        var check = await ClaudeLaunchCheckAsync();
        if (check.Refusal is { } refusal)
        {
            _setup.ShowStarted(refusal);
            SetStatus(refusal);
            _setup.RunFinished();
            return null;
        }

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
            AppLog.Write($"run not written: {ex.GetType().Name}");
            _setup.ShowStarted($"Not started: {ex.Message}");
            SetStatus($"Not started: {ex.Message}");
            _setup.RunFinished();
            return null;
        }
        AppLog.Write($"run {run.RunId} written: {run.Specs.Count} session(s)");
        // A run that waits (a start time, or the usage limit's reset) does not start on the next pass, so the line says when it starts.
        var now = DateTimeOffset.UtcNow;
        var when = run.StartAt is { } startAt && RunStore.IsWaiting(run, now)
            ? $"They start at {StartTime.Describe(startAt, now)}."
            : _watching ? "The window that supervises this state starts them." : "They start on the next pass, within about 15 seconds.";
        _setup.ShowStarted(WithWarning(check.Warning, $"Run {run.RunId} written: {run.Specs.Count} session(s). {when}"));
        try
        {
            await PassAndRefreshAsync();
        }
        finally
        {
            _setup.RunFinished();
        }
        return check;
    }

    /// <summary>
    /// Restarts every background session onto the installed claude (round 7 item 2). Only this confirmed click runs `claude respawn --all`:
    /// the owner answers the question first, and the supervisor never does it. The command runs on a pool thread, and the pass runs after.
    /// </summary>
    private async void RespawnButton_Click(object sender, RoutedEventArgs e)
    {
        if (_drift is not { } drift || _watching) return;
        var answer = MessageBox.Show(this, RespawnHold.ConfirmationText(drift.CliVersion),
            "Respawn all background sessions", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        // The hold covers the respawn and 20 seconds after it: the workers restart meanwhile, and a pass must not start or resume a session
        // that is only missing from the listing for now (round 8 item 3).
        RespawnButton.IsEnabled = false;
        _respawnHold.Begin();
        try
        {
            var result = await Task.Run(() => _cli.RespawnAllAsync());
            var text = result.Succeeded
                ? "respawn --all done: the background sessions run the installed claude"
                : $"respawn --all failed (exit {result.ExitCode}): {result.StdErr.Trim()} {result.StdOut.Trim()}".TrimEnd();
            AppLog.Write(result.Succeeded ? text : $"respawn --all failed (exit {result.ExitCode})");
            SetStatus(text);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"respawn --all could not run: {ex.GetType().Name}");
            SetStatus($"respawn --all could not run: {ex.Message}");
        }
        finally
        {
            _respawnHold.End();
            RespawnButton.IsEnabled = true;
        }
        await PassAndRefreshAsync();
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
            AppLog.Write($"failed starts not forgotten: {ex.GetType().Name}");
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
    /// <summary>--rail-tag: the rail starts with this tag filter, for the screenshots only (tags plan, Part A).</summary>
    string? FilterTag = null,
    /// <summary>--rail-group tag: the History section starts grouped by tag rather than by folder, for the screenshots only (features batch G4).</summary>
    string? GroupName = null,
    string? SetupDetail = null,
    string? SelfTestMode = null,
    string? OpenSessionId = null,
    string? ClaudeHome = null,
    TestScreenRequest? OpenTab = null,
    /// <summary>--effort-open: the transcript's "Where the effort went" panel starts expanded, for the screenshots (round 5 item 16).</summary>
    bool EffortOpen = false,
    /// <summary>--find (test only): the shown transcript opens its find bar with this query, for the screenshots (find and jump batch, T4).</summary>
    string? FindText = null,
    /// <summary>--search (test only): the rail's search box starts a search with this text, for the screenshots (keyboard batch T1 and T2).</summary>
    string? SearchText = null,
    /// <summary>--soak-seconds: the interval of the soak log line, in seconds (perf batch Q1). 0 turns the periodic line off; the start and close lines stay.</summary>
    int SoakSeconds = SessionWindow.DefaultSoakSeconds,
    /// <summary>--rail-scroll (test only): the rail is scrolled to this offset after the first pass, so the soak log shows whether a rebuild resets it (perf batch Q5).</summary>
    double RailScrollTo = 0,
    /// <summary>--codex-resume (test only): once the Codex history has loaded, the Codex resume screen opens on its first session, for the screenshots.</summary>
    bool CodexResume = false,
    /// <summary>--codex-resume summary (test only): that screen opens in the summary mode and presses Make summary once, for the screenshots.</summary>
    bool CodexResumeSummary = false,
    /// <summary>--codex-view (test only): the Codex rail starts on this view (all, history or archived), for the screenshots (parity P11).</summary>
    string? CodexViewName = null,
    /// <summary>--codex-transcript (test only): once the Codex history has loaded, the transcript of its first session opens, for the screenshots (parity P7).</summary>
    bool CodexTranscript = false,
    /// <summary>--codex-search (test only): once the Codex history has loaded, the box is set to this text, so the Codex search runs and its results show (parity P6).</summary>
    string? CodexSearchText = null,
    /// <summary>--copilot-resume (test only): once the Copilot history has loaded, the Copilot resume screen opens on its first session, for the screenshots.</summary>
    bool CopilotResume = false,
    /// <summary>--copilot-resume (test only): the mode the screenshot shows: full, summary, or empty for the last turns (the default).</summary>
    string CopilotResumeShot = "")
{
    /// <summary>The folder of the transcripts: the owner's, unless a test home is given (--claude-home).</summary>
    public string ProjectsRoot => ClaudeHome is null ? TranscriptReader.DefaultProjectsRoot : TestHome.ProjectsRootUnder(ClaudeHome);

    /// <summary>The folder of the archive: the owner's, unless a test home is given (--claude-home).</summary>
    public string ArchiveRoot => ClaudeHome is null ? TranscriptArchive.DefaultRoot : TestHome.ArchiveRootUnder(ClaudeHome);

    /// <summary>
    /// What the history index read from each transcript, kept between starts (round 10 item 2). It sits in a cache folder beside the state file,
    /// so the owner's state keeps the owner's cache and a run with its own state keeps its own.
    /// </summary>
    public string IndexCachePath => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(StatePath))!, "cache", "transcript-index.json");

    /// <summary>What the limit history read from each transcript, kept between reads (perf plan 2026-11-18). It sits beside the index cache.</summary>
    public string LimitHistoryCachePath => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(StatePath))!, "cache", "limit-history.json");

    public static LaunchOptions Parse(string[] args)
    {
        string? workdir = null, task = null, state = null, shot = null, selfTest = null, filterName = null, filterTag = null, groupName = null, selfTestMode = null, setupDetail = null, openSession = null, claudeHome = null, openTab = null, findText = null, searchText = null;
        double shotAfter = 8;
        int soakSeconds = SessionWindow.DefaultSoakSeconds;
        double railScroll = 0;
        string? codexView = null, codexSearch = null;
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
                case "--rail-tag": filterTag = args[++i]; break;
                case "--rail-group": groupName = args[++i]; break;
                case "--setup-detail": setupDetail = args[++i]; break;
                case "--open-transcript": openSession = args[++i]; break;
                case "--claude-home": claudeHome = args[++i]; break;
                case "--open-tab": openTab = args[++i]; break;
                case "--find": findText = args[++i]; break;
                case "--search": searchText = args[++i]; break;
                case "--soak-seconds": soakSeconds = int.Parse(args[++i], inv); break;
                case "--rail-scroll": railScroll = double.Parse(args[++i], inv); break;
                case "--codex-view": codexView = args[++i]; break;
                case "--codex-search": codexSearch = args[++i]; break;
            }
        }
        // --codex-resume takes an optional value, summary (test only): the flag's position is read here, not in the switch above.
        var codexResumeAt = Array.IndexOf(args, "--codex-resume");
        // --copilot-resume takes an optional value, full or summary (test only), as --codex-resume takes summary.
        var copilotResumeAt = Array.IndexOf(args, "--copilot-resume");
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
            FilterTag: filterTag,
            GroupName: groupName,
            SetupDetail: setupDetail,
            SelfTestMode: selfTestMode,
            OpenSessionId: openSession,
            ClaudeHome: claudeHome,
            OpenTab: TestScreenRequest.Parse(openTab),
            EffortOpen: args.Contains("--effort-open"),
            FindText: findText,
            SearchText: searchText,
            SoakSeconds: soakSeconds,
            RailScrollTo: railScroll,
            CodexResume: codexResumeAt >= 0,
            CodexResumeSummary: codexResumeAt >= 0 && codexResumeAt + 1 < args.Length && args[codexResumeAt + 1] == "summary",
            CodexViewName: codexView,
            CodexTranscript: args.Contains("--codex-transcript"),
            CodexSearchText: codexSearch,
            CopilotResume: copilotResumeAt >= 0,
            CopilotResumeShot: copilotResumeAt >= 0 && copilotResumeAt + 1 < args.Length ? args[copilotResumeAt + 1] : string.Empty);
    }
}

/// <summary>True when the process is a test host (the test runner), so code that starts or resumes real sessions can refuse to run there.</summary>
internal static class RunningUnderTest
{
    /// <summary>True when this process is the test runner's own host (testhost), whether or not the flag is set.</summary>
    public static bool IsTestHost { get; } =
        System.Diagnostics.Process.GetCurrentProcess().ProcessName.StartsWith("testhost", StringComparison.OrdinalIgnoreCase)
        || (System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name?.StartsWith("testhost", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>True when the OPENTAIL_BULLSHARK_UNDER_TEST flag is set to 1 in the environment (the test guard sets it).</summary>
    public static bool FlagSet { get; } =
        string.Equals(Environment.GetEnvironmentVariable("OPENTAIL_BULLSHARK_UNDER_TEST"), "1", StringComparison.Ordinal);

    /// <summary>True in a test host, or when the flag is set. A window that would start sessions refuses to run then.</summary>
    public static bool Is => FlagSet || IsTestHost;
}
