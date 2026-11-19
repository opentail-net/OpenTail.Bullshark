using System.Diagnostics;
using System.Windows.Threading;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The soak log (perf and testability batch, Q1): one line when the first pass has finished, then one every 30 minutes (or every
/// <c>--soak-seconds</c>), and one when the window closes. Each line holds counts and times only: the memory, the handles and threads of the
/// process, the terminals, rail items and transcript blocks held, the passes run and the time of the last pass and of the last rail build, and
/// the ui.json reads since the line before. It never holds a session, folder, file or message text. The startup phases are logged once each, as
/// the time after the process started.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The interval of the periodic soak line when <c>--soak-seconds</c> is not given: 30 minutes.</summary>
    public const int DefaultSoakSeconds = 1800;

    private const double MegaByte = 1024.0 * 1024.0;

    private DispatcherTimer? _soakTimer;
    private DateTime _processStart;
    private int _passesRun;
    private double _lastPassMs = -1;
    private double _lastRailMs = -1;
    private int _railBuilds;
    private readonly HashSet<string> _startupNoted = new(StringComparer.Ordinal);
    private int _passesAtSoakLine;
    private (long UiThread, long Background) _readsAtSoakLine;

    // The rail builds and passes since the line before: their count, sum and largest time (perf batch Q1).
    private int _railsSinceLine;
    private double _railMsSinceLine;
    private double _railMaxSinceLine;
    private int _passesSinceTimed;
    private double _passMsSinceLine;
    private double _passMaxSinceLine;

    private void InitSoak()
    {
        using (var self = Process.GetCurrentProcess()) _processStart = self.StartTime;
        ContentRendered += (_, _) => NoteStartup("window shown");
        if (_options.SoakSeconds <= 0) return;
        _soakTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_options.SoakSeconds) };
        _soakTimer.Tick += (_, _) => WriteSoakLine("periodic");
        _soakTimer.Start();
    }

    /// <summary>Logs one startup phase, once, with the time since the process started (perf batch Q1). Counts and times only.</summary>
    private void NoteStartup(string phase)
    {
        if (!_startupNoted.Add(phase)) return;
        AppLog.Write($"startup: {phase} {(DateTime.Now - _processStart).TotalMilliseconds:0} ms after the process started");
    }

    /// <summary>Called when a pass has finished: its time, and the first pass's phase.</summary>
    private void NotePassDone(double ms)
    {
        _lastPassMs = ms;
        _passesRun++;
        _passesSinceTimed++;
        _passMsSinceLine += ms;
        _passMaxSinceLine = Math.Max(_passMaxSinceLine, ms);
        if (_passesRun == 1)
        {
            NoteStartup("first pass done");
            WriteSoakLine("start");
            // The test flag --rail-scroll puts the rail's scroll at an offset once it is laid out, so the soak line shows whether a rebuild resets it.
            if (_options.RailScrollTo > 0) Dispatcher.BeginInvoke(new Action(() => ScrollRailTo(_options.RailScrollTo)), DispatcherPriority.Loaded);
        }
    }

    /// <summary>Called when the rail has been built: its time. The first build is a startup phase.</summary>
    private void NoteRailBuilt(double ms)
    {
        _lastRailMs = ms;
        _railBuilds++;
        _railsSinceLine++;
        _railMsSinceLine += ms;
        _railMaxSinceLine = Math.Max(_railMaxSinceLine, ms);
        if (_railBuilds == 1) NoteStartup("rail built");
    }

    /// <summary>Writes the soak line. Counts and times only; the reads and passes are counted since the line before.</summary>
    private void WriteSoakLine(string trigger)
    {
        using var self = Process.GetCurrentProcess();
        var (uiReads, backgroundReads) = UiFile.ReadCounts;
        var uiDelta = uiReads - _readsAtSoakLine.UiThread;
        var backgroundDelta = backgroundReads - _readsAtSoakLine.Background;
        var passDelta = _passesRun - _passesAtSoakLine;
        var uptime = DateTime.Now - _processStart;
        AppLog.Write(
            $"soak ({trigger}): uptime {uptime:hh\\:mm\\:ss}" +
            $" · gc heap {GC.GetTotalMemory(false) / MegaByte:0.0} MB" +
            $" · working set {self.WorkingSet64 / MegaByte:0.0} MB" +
            $" · private {self.PrivateMemorySize64 / MegaByte:0.0} MB" +
            $" · handles {self.HandleCount}" +
            $" · threads {self.Threads.Count}" +
            $" · terminals {_terminals.Count}" +
            $" · rail items {AllItems(Rail.Items).Count()}" +
            $" · transcript blocks {_transcript.BlockCount}" +
            $" · passes {_passesRun} (+{passDelta}, mean {Mean(_passMsSinceLine, _passesSinceTimed):0} ms, max {_passMaxSinceLine:0} ms)" +
            $" · last pass {_lastPassMs:0} ms" +
            $" · rail builds {_railBuilds} (+{_railsSinceLine}, mean {Mean(_railMsSinceLine, _railsSinceLine):0.0} ms, max {_railMaxSinceLine:0} ms)" +
            $" · rail kept {_railsKept}, rebuilds while scrolled {_railRebuildsWhileScrolled}, rail scroll {RailScrollOffset():0} px" +
            $" · last rail {_lastRailMs:0.0} ms" +
            $" · ui.json reads +{uiDelta + backgroundDelta} (UI thread +{uiDelta}, background +{backgroundDelta})");
        _passesAtSoakLine = _passesRun;
        _readsAtSoakLine = (uiReads, backgroundReads);
        _railsSinceLine = 0;
        _railMsSinceLine = 0;
        _railMaxSinceLine = 0;
        _passesSinceTimed = 0;
        _passMsSinceLine = 0;
        _passMaxSinceLine = 0;
    }

    private static double Mean(double sum, int count) => count == 0 ? 0 : sum / count;
}
