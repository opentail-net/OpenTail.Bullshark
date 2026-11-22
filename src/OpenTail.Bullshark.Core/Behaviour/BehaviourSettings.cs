namespace OpenTail.Bullshark.Core.Behaviour;

/// <summary>When a banner and its sound are held back: the app window's focus, and whether the session is the one the rail selects (<c>alerts.focusGate</c>).</summary>
public enum FocusGate
{
    /// <summary>"unfocused": no banner and no sound while the app window is in front.</summary>
    Unfocused,

    /// <summary>"unfocusedOrUnselected": also no banner and no sound for the session the rail has selected (see <see cref="AlertRules.Shows"/>).</summary>
    UnfocusedOrUnselected,
}

/// <summary>
/// The kinds of change that can alert (<c>alerts.kinds</c>). <see cref="Notice"/> is not a setting: the overlap, conflict, run-limit, silence and usage-limit
/// notices alert as they did before, and the focus gate still applies to them.
/// </summary>
public enum AlertGroup
{
    /// <summary>A session waits for an answer: blocked on a permission or a question ("needsInput").</summary>
    NeedsInput,

    /// <summary>A session failed, stopped, could not start or lost its name, or a supervisor pass failed ("errored").</summary>
    Errored,

    /// <summary>A coder finished with changes and is ready for review ("finished"). Off by default.</summary>
    Finished,

    /// <summary>A notice, which is not a setting. It always alerts, as before.</summary>
    Notice,
}

/// <summary>What to do when a Claude or Codex version cannot be read (<c>versions.unreadable</c>).</summary>
public enum UnreadableVersion
{
    /// <summary>"warn": start, with a visible warning.</summary>
    Warn,

    /// <summary>"refuse": do not start.</summary>
    Refuse,
}

/// <summary>What a resume does when more than one transcript matches (<c>resume.ambiguous</c>).</summary>
public enum AmbiguousResume
{
    /// <summary>"refuse": the match is refused and the owner picks.</summary>
    Refuse,

    /// <summary>"earliest": the earliest candidate is used.</summary>
    Earliest,
}

/// <summary>The stamp of a folder that tells the session list whether it changed (<c>index.stamp</c>).</summary>
public enum IndexStamp
{
    /// <summary>"countAndNewest": the file count and the newest change time.</summary>
    CountAndNewest,
}

/// <summary>When a usage total is attributed to a session (<c>usage.attribution</c>).</summary>
public enum UsageAttribution
{
    /// <summary>"lastUpdate": to the session's last update.</summary>
    LastUpdate,

    /// <summary>"start": to the session's start.</summary>
    Start,

    /// <summary>"event": to each event as it happened.</summary>
    Event,
}

/// <summary>
/// The alert settings (behaviour batch, R2). <see cref="RepeatMinutes"/> is the re-alert interval, and the cooldown of a repeated alert.
/// </summary>
public sealed record AlertsSettings
{
    /// <summary>Minutes between re-alerts for a session that keeps waiting (<c>alerts.repeatMinutes</c>). Zero means never re-alert. Default 0.</summary>
    public int RepeatMinutes { get; init; } = 0;

    /// <summary>The focus gate (<c>alerts.focusGate</c>). Default Unfocused.</summary>
    public FocusGate FocusGate { get; init; } = FocusGate.Unfocused;

    /// <summary>The kinds that alert (<c>alerts.kinds</c>). Default needs-input and errored; finished is off.</summary>
    public IReadOnlySet<AlertGroup> Kinds { get; init; } = new HashSet<AlertGroup> { AlertGroup.NeedsInput, AlertGroup.Errored };

    /// <summary>The re-alert interval; zero when off.</summary>
    public TimeSpan RepeatInterval => TimeSpan.FromMinutes(RepeatMinutes);
}

/// <summary>The lookup settings (behaviour batch, R10).</summary>
public sealed record LookupsSettings
{
    /// <summary>Seconds a missing session id is remembered before it is searched again (<c>lookups.missMemorySeconds</c>). Zero is off. Default 60.</summary>
    public int MissMemorySeconds { get; init; } = 60;

    /// <summary>The memory as a span; zero when off.</summary>
    public TimeSpan MissMemory => TimeSpan.FromSeconds(MissMemorySeconds);
}

/// <summary>The minimum versions of the tools (behaviour batch, R4): the version each one was tested against.</summary>
public sealed record MinimumVersions
{
    /// <summary>The minimum Claude Code version (<c>versions.minimum.claude</c>). Default "2.1.330".</summary>
    public string Claude { get; init; } = "2.1.330";

    /// <summary>The minimum Codex version (<c>versions.minimum.codex</c>). Default "0.171.0".</summary>
    public string Codex { get; init; } = "0.171.0";
}

/// <summary>The version settings (behaviour batch, R4).</summary>
public sealed record VersionsSettings
{
    /// <summary>The minimum version of each tool.</summary>
    public MinimumVersions Minimum { get; init; } = new();

    /// <summary>What a version that cannot be read does (<c>versions.unreadable</c>). Default <see cref="UnreadableVersion.Warn"/>.</summary>
    public UnreadableVersion Unreadable { get; init; } = UnreadableVersion.Warn;

    /// <summary>
    /// Seconds a version reading that is not Supported (too old, or could not be read) is kept before the command runs again (<c>versions.recheckSeconds</c>).
    /// Zero means the command runs on every launch check. Default 60.
    /// </summary>
    public int RecheckSeconds { get; init; } = 60;

    /// <summary>The recheck window as a span; zero when off.</summary>
    public TimeSpan RecheckWindow => TimeSpan.FromSeconds(RecheckSeconds);
}

/// <summary>The exit settings (behaviour batch, R6).</summary>
public sealed record ExitSettings
{
    /// <summary>Whether a session's tab is marked ended when its process exits (<c>exit.endOnProcessExit</c>). Default true.</summary>
    public bool EndOnProcessExit { get; init; } = true;
}

/// <summary>The resume settings (behaviour batch, R7).</summary>
public sealed record ResumeSettings
{
    /// <summary>Whether a resume may fall back to the newest transcript in the folder (<c>resume.allowNewestInFolder</c>). Default false.</summary>
    public bool AllowNewestInFolder { get; init; } = false;

    /// <summary>What an ambiguous match does (<c>resume.ambiguous</c>). Default <see cref="AmbiguousResume.Refuse"/>.</summary>
    public AmbiguousResume Ambiguous { get; init; } = AmbiguousResume.Refuse;
}

/// <summary>The state settings (behaviour batch, R5).</summary>
public sealed record StateSettings
{
    /// <summary>Minutes after which a "working" state decays (<c>state.workingDecayMinutes</c>). Zero means never. Default 0.</summary>
    public int WorkingDecayMinutes { get; init; } = 0;

    /// <summary>Whether a clock that goes backwards is clamped to zero (<c>state.clampClockBackwards</c>). Default true.</summary>
    public bool ClampClockBackwards { get; init; } = true;
}

/// <summary>The index settings (behaviour batch, R9).</summary>
public sealed record IndexSettings
{
    /// <summary>Seconds between refreshes of the session list (<c>index.refreshSeconds</c>). Zero means only when the window opens. Default 60.</summary>
    public int RefreshSeconds { get; init; } = 60;

    /// <summary>The folder stamp (<c>index.stamp</c>). Default <see cref="IndexStamp.CountAndNewest"/>.</summary>
    public IndexStamp Stamp { get; init; } = IndexStamp.CountAndNewest;
}

/// <summary>The usage settings (behaviour batch, R8).</summary>
public sealed record UsageSettings
{
    /// <summary>When usage is attributed to a session (<c>usage.attribution</c>). Default <see cref="UsageAttribution.LastUpdate"/>.</summary>
    public UsageAttribution Attribution { get; init; } = UsageAttribution.LastUpdate;

    /// <summary>Whether reasoning tokens count as output (<c>usage.reasoningAsOutput</c>). Default true.</summary>
    public bool ReasoningAsOutput { get; init; } = true;
}

/// <summary>
/// Every behaviour setting, one section per backlog row (behaviour.json, decisions of 2026-11-22). Each field's default is set here, in code: a missing
/// file or key gives the default, and a change of default is a change here, not a configuration change.
/// </summary>
public sealed record BehaviourSettings
{
    /// <summary>The defaults of every setting.</summary>
    public static BehaviourSettings Defaults { get; } = new();

    /// <summary>The alert settings (R2).</summary>
    public AlertsSettings Alerts { get; init; } = new();

    /// <summary>The lookup settings (R10).</summary>
    public LookupsSettings Lookups { get; init; } = new();

    /// <summary>The version settings (R4).</summary>
    public VersionsSettings Versions { get; init; } = new();

    /// <summary>The exit settings (R6).</summary>
    public ExitSettings Exit { get; init; } = new();

    /// <summary>The resume settings (R7).</summary>
    public ResumeSettings Resume { get; init; } = new();

    /// <summary>The state settings (R5).</summary>
    public StateSettings State { get; init; } = new();

    /// <summary>The index settings (R9).</summary>
    public IndexSettings Index { get; init; } = new();

    /// <summary>The usage settings (R8).</summary>
    public UsageSettings Usage { get; init; } = new();
}
