using System.Globalization;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Health;

/// <summary>How one preflight row stands: fine, something to check, or not known.</summary>
public enum PreflightStatus
{
    Ok,
    Check,
    Unknown,
}

/// <summary>One row of the bedtime preflight: its name, its status, and one short line of text (no prompt text, no paths).</summary>
public sealed record PreflightRow(string Name, PreflightStatus Status, string Text);

/// <summary>The preflight: the rows in their order, and the verdict line on top ("Ready for the night", or how many things to check).</summary>
public sealed record PreflightReport(IReadOnlyList<PreflightRow> Rows, string Verdict);

/// <summary>
/// The login warning as the supervisor read it this pass. <see cref="Readable"/> is false when no live session could be read, so the login is
/// not known. <see cref="Remaining"/> is null when readable sessions show no warning.
/// </summary>
public sealed record LoginReading(bool Readable, TimeSpan? Remaining);

/// <summary>The quiet hours as the preflight shows them: <see cref="Window"/> is null when they are off, else "22:00-07:00"; <see cref="Now"/> is whether now is inside them.</summary>
public sealed record QuietState(string? Window, bool Now);

/// <summary>
/// What the preflight reads, filled in by the window (night batch N2). A plain record, so the rules are tested without a window or a disk.
/// <see cref="Runs"/> is null when runs.json cannot be read, and <see cref="SleepMinutes"/> is null when the power setting cannot be read
/// (0 means "never"). <see cref="SetupProblems"/> is null when the setup check has not run.
/// </summary>
public sealed record PreflightInput(
    DateTimeOffset Now,
    DateTimeOffset? LastPass,
    DaemonState Daemon,
    int StaleSessions,
    LoginReading Login,
    int? SetupProblems,
    IReadOnlyList<ActiveLimit> ActiveLimits,
    IReadOnlyList<RunRecord>? Runs,
    int TrustWaiting,
    int? SleepMinutes,
    int NeedsYou,
    QuietState Quiet);

/// <summary>
/// The bedtime preflight (night batch N2): whether tonight will work, as ten rows. Pure: the window fills a <see cref="PreflightInput"/>.
/// A row is "Check" when the owner should act before going to sleep; "Unknown" when the value could not be read. The verdict is "Ready for
/// the night" when no row is Check. Nothing here starts, sends or changes anything.
/// </summary>
public static class Preflight
{
    /// <summary>A last pass older than this is shown as a Check.</summary>
    public static readonly TimeSpan PassStaleAfter = TimeSpan.FromMinutes(2);

    /// <summary>The runs that start within this time of now count as due for the Runs, Usage limit and Sleep rows.</summary>
    public static readonly TimeSpan RunHorizon = TimeSpan.FromHours(12);

    /// <summary>The ten rows and the verdict, from one input.</summary>
    public static PreflightReport Build(PreflightInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var rows = new List<PreflightRow>
        {
            WindowRow(input),
            DaemonRow(input),
            LoginRow(input),
            SetupRow(input),
            LimitRow(input),
            RunsRow(input),
            TrustRow(input),
            SleepRow(input),
            NeedsRow(input),
            QuietRow(input),
        };
        return new PreflightReport(rows, VerdictOf(rows));
    }

    /// <summary>"Ready for the night" when no row is Check; otherwise how many rows are.</summary>
    public static string VerdictOf(IReadOnlyList<PreflightRow> rows)
    {
        var checks = rows.Count(row => row.Status == PreflightStatus.Check);
        return checks == 0 ? "Ready for the night" : checks == 1 ? "1 thing to check" : $"{checks} things to check";
    }

    private static PreflightRow WindowRow(PreflightInput input)
    {
        const string name = "Window/pass";
        if (input.LastPass is not { } last) return new(name, PreflightStatus.Unknown, "no pass recorded yet");
        var age = Heartbeat.Age(last, input.Now) ?? TimeSpan.Zero;
        return age > PassStaleAfter
            ? new(name, PreflightStatus.Check, $"last pass {Heartbeat.Describe(age)}: nothing has been supervised since")
            : new(name, PreflightStatus.Ok, $"last pass {Heartbeat.Describe(age)}");
    }

    private static PreflightRow DaemonRow(PreflightInput input)
    {
        const string name = "Daemon";
        return input.Daemon switch
        {
            DaemonState.Running => new(name, PreflightStatus.Ok, "running"),
            DaemonState.NotRunning when input.StaleSessions > 0 => new(name, PreflightStatus.Check,
                $"down: {Plural(input.StaleSessions, "session")} listed as working or blocked {(input.StaleSessions == 1 ? "is" : "are")} not alive"),
            DaemonState.NotRunning => new(name, PreflightStatus.Ok, "idle: it starts when a session needs it"),
            _ => new(name, PreflightStatus.Unknown, "could not be read"),
        };
    }

    private static PreflightRow LoginRow(PreflightInput input)
    {
        const string name = "Login";
        if (!input.Login.Readable) return new(name, PreflightStatus.Unknown, "not read: no live session could be read");
        if (input.Login.Remaining is not { } remaining) return new(name, PreflightStatus.Ok, "no expiry warning on screen");
        return LoginExpiry.IsExpiringSoon(remaining)
            ? new(name, PreflightStatus.Check, $"expires in {Left(remaining)}: run /login")
            : new(name, PreflightStatus.Ok, $"{Left(remaining)} left");
    }

    private static PreflightRow SetupRow(PreflightInput input)
    {
        const string name = "Setup check";
        return input.SetupProblems switch
        {
            null => new(name, PreflightStatus.Unknown, "not run yet"),
            0 => new(name, PreflightStatus.Ok, "no problems"),
            var count => new(name, PreflightStatus.Check, $"{Plural(count.Value, "problem")}: open Setup"),
        };
    }

    private static PreflightRow LimitRow(PreflightInput input)
    {
        const string name = "Usage limit";
        var inForce = input.ActiveLimits.Where(limit => limit.ResetsAt > input.Now).OrderByDescending(limit => limit.ResetsAt).ToList();
        if (inForce.Count == 0) return new(name, PreflightStatus.Ok, "none in force");
        if (input.Runs is null) return new(name, PreflightStatus.Unknown, "a limit is in force; runs.json could not be read");

        var governing = inForce[0];
        var more = inForce.Count > 1 ? $" (and {inForce.Count - 1} more)" : string.Empty;
        var label = $"{LimitStatus.LabelOf(governing.Type)} limit until {Clock(governing.ResetsAt, input.Now)}{more}";
        var due = input.Runs.Count(run => run.Specs.Count > 0 && StartOf(run, input.Now) < governing.ResetsAt);
        return due > 0
            ? new(name, PreflightStatus.Check, $"{label}: {Plural(due, "run")} due before it resets")
            : new(name, PreflightStatus.Ok, $"{label}; no run due before it resets");
    }

    private static PreflightRow RunsRow(PreflightInput input)
    {
        const string name = "Runs";
        if (input.Runs is null) return new(name, PreflightStatus.Unknown, "runs.json could not be read");

        var horizon = input.Now + RunHorizon;
        var starting = input.Runs
            .Where(run => run.Specs.Count > 0 && RunStore.IsWaiting(run, input.Now) && run.StartAt <= horizon)
            .Select(run => run.StartAt!.Value)
            .OrderBy(at => at)
            .ToList();
        if (starting.Count == 0) return new(name, PreflightStatus.Ok, "none waiting to start in the next 12 h");
        return new(name, PreflightStatus.Ok,
            $"{Plural(starting.Count, "run")} due in the next 12 h; the first at {Clock(starting[0], input.Now)}");
    }

    private static PreflightRow TrustRow(PreflightInput input)
    {
        const string name = "Trust";
        return input.TrustWaiting > 0
            ? new(name, PreflightStatus.Check, $"{Plural(input.TrustWaiting, "folder")} waiting for trust; their runs will not start")
            : new(name, PreflightStatus.Ok, "no folder waits for trust");
    }

    private static PreflightRow SleepRow(PreflightInput input)
    {
        const string name = "Sleep";
        if (input.SleepMinutes is not { } minutes) return new(name, PreflightStatus.Unknown, "the power setting could not be read");
        if (minutes == 0) return new(name, PreflightStatus.Ok, "never sleeps on AC power");

        var live = input.Runs is not null && input.Runs.Any(run => run.Specs.Count > 0 && !RunStore.IsWaiting(run, input.Now));
        var due = input.Runs is not null && input.Runs.Any(run => run.Specs.Count > 0 && RunStore.IsWaiting(run, input.Now)
            && run.StartAt <= input.Now + RunHorizon);
        return live || due
            ? new(name, PreflightStatus.Check, $"sleeps after {minutes} min on AC power while a run is due or live")
            : new(name, PreflightStatus.Ok, $"sleeps after {minutes} min on AC power; no run is due in the next 12 h");
    }

    private static PreflightRow NeedsRow(PreflightInput input) => input.NeedsYou > 0
        ? new("Needs you", PreflightStatus.Check, $"{Plural(input.NeedsYou, "session")} waiting now")
        : new("Needs you", PreflightStatus.Ok, "nothing waits for you");

    private static PreflightRow QuietRow(PreflightInput input)
    {
        const string name = "Quiet hours";
        if (input.Quiet.Window is not { } window) return new(name, PreflightStatus.Ok, "off: sound, flash and toasts are on");
        return input.Quiet.Now
            ? new(name, PreflightStatus.Ok, $"{window}: quiet now, so sound, flash and toasts are off")
            : new(name, PreflightStatus.Ok, $"{window}: not quiet now");
    }

    /// <summary>When a run starts: its start time while it waits, else now (it is due or running).</summary>
    private static DateTimeOffset StartOf(RunRecord run, DateTimeOffset now) =>
        RunStore.IsWaiting(run, now) ? run.StartAt!.Value : now;

    /// <summary>"1 day", "2 days": the count and the noun.</summary>
    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? string.Empty : "s")}";

    /// <summary>A span as the owner reads it for a login: days when there are a day or more, else hours.</summary>
    private static string Left(TimeSpan span) =>
        span.TotalDays >= 1 ? Plural((int)span.TotalDays, "day") : Plural(Math.Max(0, (int)span.TotalHours), "hour");

    /// <summary>The local clock time, with the day name when it is not the day of <paramref name="now"/>.</summary>
    private static string Clock(DateTimeOffset at, DateTimeOffset now)
    {
        var local = at.ToLocalTime();
        return local.Date == now.ToLocalTime().Date
            ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("ddd HH:mm", CultureInfo.InvariantCulture);
    }
}
