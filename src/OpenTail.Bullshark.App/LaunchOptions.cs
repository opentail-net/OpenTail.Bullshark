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
