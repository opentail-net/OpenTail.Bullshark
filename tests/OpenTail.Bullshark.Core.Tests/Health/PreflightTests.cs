using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Health;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Health;

/// <summary>
/// The bedtime preflight (night batch N2): each row in its three states, the verdict, and the inputs the rows read. The inputs are built here, so
/// no window, disk or `claude` command is needed.
/// </summary>
public sealed class PreflightTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 2, 0, 0, TimeSpan.Zero);

    private static readonly SessionSpec Spec = new(SessionRole.Coder, "bullshark-coder-a", "C:\\work", "claude-haiku-5-5", "high", "brief", "auto");

    private static RunRecord Run(string id, DateTimeOffset? startAt = null, bool withSessions = true) => new(
        id, "pattern", "C:\\work", "auto", Now.AddDays(-1), withSessions ? new[] { Spec } : Array.Empty<SessionSpec>(), StartAt: startAt);

    /// <summary>An input where every row is Ok, so one test can change one thing.</summary>
    private static PreflightInput Healthy(
        DateTimeOffset? lastPass = null,
        DaemonState daemon = DaemonState.Running,
        int stale = 0,
        LoginReading? login = null,
        int? setup = 0,
        IReadOnlyList<ActiveLimit>? limits = null,
        IReadOnlyList<RunRecord>? runs = null,
        int trust = 0,
        int? sleep = 0,
        int needsYou = 0,
        QuietState? quiet = null) => new(
            Now,
            lastPass ?? Now.AddSeconds(-12),
            daemon,
            stale,
            login ?? new LoginReading(Readable: true, Remaining: null),
            setup,
            limits ?? [],
            runs ?? [],
            trust,
            sleep,
            needsYou,
            quiet ?? new QuietState(null, false));

    private static PreflightRow RowOf(PreflightInput input, string name) =>
        RowOf(Preflight.Build(input).Rows, name);

    private static PreflightRow RowOf(IReadOnlyList<PreflightRow> rows, string name) =>
        rows.Single(row => row.Name == name);

    [Fact]
    public void AHealthyNightHasTenOkRowsAndTheVerdictReady()
    {
        var report = Preflight.Build(Healthy());

        Assert.Equal(10, report.Rows.Count);
        Assert.All(report.Rows, row => Assert.Equal(PreflightStatus.Ok, row.Status));
        Assert.Equal("Ready for the night", report.Verdict);
        Assert.Equal(
            ["Window/pass", "Daemon", "Login", "Setup check", "Usage limit", "Runs", "Trust", "Sleep", "Needs you", "Quiet hours"],
            report.Rows.Select(row => row.Name).ToArray());
    }

    [Theory]
    [InlineData(0, "Ready for the night")]
    [InlineData(1, "1 thing to check")]
    [InlineData(2, "2 things to check")]
    public void TheVerdictCountsTheCheckRows(int checks, string expected)
    {
        var rows = Enumerable.Range(0, 3).Select(i => new PreflightRow($"r{i}", i < checks ? PreflightStatus.Check : PreflightStatus.Ok, "t")).ToList();

        Assert.Equal(expected, Preflight.VerdictOf(rows));
    }

    [Fact]
    public void UnknownRowsDoNotStopTheVerdict()
    {
        var report = Preflight.Build(Healthy(setup: null, sleep: null));

        Assert.Equal(PreflightStatus.Unknown, RowOf(report.Rows, "Setup check").Status);
        Assert.Equal(PreflightStatus.Unknown, RowOf(report.Rows, "Sleep").Status);
        Assert.Equal("Ready for the night", report.Verdict);
    }

    [Fact]
    public void TwoCheckRowsMakeTwoThingsToCheck()
    {
        var report = Preflight.Build(Healthy(needsYou: 1, trust: 1));

        Assert.Equal("2 things to check", report.Verdict);
    }

    [Fact]
    public void WindowPassIsOkUnderTwoMinutesAndCheckAfter()
    {
        var ok = RowOf(Healthy(lastPass: Now.AddMinutes(-1).AddSeconds(-59)), "Window/pass");
        Assert.Equal(PreflightStatus.Ok, ok.Status);
        Assert.Equal("last pass 1 min ago", ok.Text);

        var late = RowOf(Healthy(lastPass: Now.AddMinutes(-3)), "Window/pass");
        Assert.Equal(PreflightStatus.Check, late.Status);
        Assert.Equal("last pass 3 min ago: nothing has been supervised since", late.Text);
    }

    [Fact]
    public void WindowPassIsUnknownWithoutAHeartbeat()
    {
        Assert.Equal(PreflightStatus.Unknown, RowOf(Healthy() with { LastPass = null }, "Window/pass").Status);
    }

    [Fact]
    public void DaemonIsOkRunningOrIdleAndUnknownWhenUnread()
    {
        Assert.Equal(PreflightStatus.Ok, RowOf(Healthy(daemon: DaemonState.Running), "Daemon").Status);
        Assert.Equal(PreflightStatus.Ok, RowOf(Healthy(daemon: DaemonState.NotRunning), "Daemon").Status);
        Assert.Equal(PreflightStatus.Unknown, RowOf(Healthy(daemon: DaemonState.Unknown), "Daemon").Status);
    }

    [Fact]
    public void DaemonIsDownWhenSessionsClaimToBeAliveWhileItIsNotRunning()
    {
        var row = RowOf(Healthy(daemon: DaemonState.NotRunning, stale: 2), "Daemon");

        Assert.Equal(PreflightStatus.Check, row.Status);
        Assert.Equal("down: 2 sessions listed as working or blocked are not alive", row.Text);
    }

    [Fact]
    public void LoginIsOkCheckWhenExpiringAndUnknownWhenUnread()
    {
        Assert.Equal(PreflightStatus.Ok, RowOf(Healthy(login: new LoginReading(true, null)), "Login").Status);
        Assert.Equal("no expiry warning on screen", RowOf(Healthy(login: new LoginReading(true, null)), "Login").Text);

        var fine = RowOf(Healthy(login: new LoginReading(true, TimeSpan.FromDays(5))), "Login");
        Assert.Equal(PreflightStatus.Ok, fine.Status);
        Assert.Equal("5 days left", fine.Text);

        var soon = RowOf(Healthy(login: new LoginReading(true, TimeSpan.FromDays(2))), "Login");
        Assert.Equal(PreflightStatus.Check, soon.Status);
        Assert.Equal("expires in 2 days: run /login", soon.Text);

        Assert.Equal(PreflightStatus.Unknown, RowOf(Healthy(login: new LoginReading(false, null)), "Login").Status);
    }

    [Fact]
    public void SetupCheckIsOkCheckWithProblemsAndUnknownWhenNotRun()
    {
        Assert.Equal(PreflightStatus.Ok, RowOf(Healthy(setup: 0), "Setup check").Status);

        var one = RowOf(Healthy(setup: 1), "Setup check");
        Assert.Equal(PreflightStatus.Check, one.Status);
        Assert.Equal("1 problem: open Setup", one.Text);

        Assert.Equal(PreflightStatus.Unknown, RowOf(Healthy(setup: null), "Setup check").Status);
    }

    [Fact]
    public void UsageLimitIsOkWithNoneInForceAndCheckWhenARunIsDueBeforeTheReset()
    {
        Assert.Equal("none in force", RowOf(Healthy(), "Usage limit").Text);

        var reset = Now.AddHours(3);
        var limit = new ActiveLimit("five_hour", reset);

        // A run that starts after the reset is not due before it.
        var later = RowOf(Healthy(limits: [limit], runs: [Run("late", startAt: reset.AddMinutes(10))]), "Usage limit");
        Assert.Equal(PreflightStatus.Ok, later.Status);

        // A run that starts before the reset, and one already live, are due before it.
        var early = RowOf(Healthy(limits: [limit], runs: [Run("early", startAt: Now.AddHours(1))]), "Usage limit");
        Assert.Equal(PreflightStatus.Check, early.Status);
        Assert.StartsWith("5-hour limit until ", early.Text);
        Assert.EndsWith(": 1 run due before it resets", early.Text);

        Assert.Equal(PreflightStatus.Check, RowOf(Healthy(limits: [limit], runs: [Run("live")]), "Usage limit").Status);
    }

    [Fact]
    public void UsageLimitIgnoresALimitThatHasReset()
    {
        var expired = new ActiveLimit("weekly", Now.AddMinutes(-1));

        Assert.Equal("none in force", RowOf(Healthy(limits: [expired], runs: [Run("live")]), "Usage limit").Text);
    }

    [Fact]
    public void UsageLimitIsUnknownWhenRunsCannotBeRead()
    {
        var input = Healthy(limits: [new ActiveLimit("weekly", Now.AddDays(2))]) with { Runs = null };

        Assert.Equal(PreflightStatus.Unknown, RowOf(input, "Usage limit").Status);
    }

    [Fact]
    public void RunsCountsTheWaitingRunsInTheNextTwelveHours()
    {
        var none = RowOf(Healthy(runs: [Run("live")]), "Runs");
        Assert.Equal(PreflightStatus.Ok, none.Status);
        Assert.Equal("none waiting to start in the next 12 h", none.Text);

        var two = RowOf(Healthy(runs: [Run("a", Now.AddHours(9)), Run("b", Now.AddHours(7)), Run("far", Now.AddHours(13))]), "Runs");
        Assert.Equal(PreflightStatus.Ok, two.Status);
        Assert.StartsWith("2 runs due in the next 12 h; the first at ", two.Text);
        Assert.EndsWith(Now.AddHours(7).ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), two.Text);

        Assert.Equal(PreflightStatus.Unknown, RowOf(Healthy() with { Runs = null }, "Runs").Status);
    }

    [Fact]
    public void ARunWithNoSessionsIsNotCounted()
    {
        var row = RowOf(Healthy(runs: [Run("empty", Now.AddHours(2), withSessions: false)]), "Runs");

        Assert.Equal("none waiting to start in the next 12 h", row.Text);
    }

    [Fact]
    public void TrustIsCheckWhenAFolderWaits()
    {
        Assert.Equal(PreflightStatus.Ok, RowOf(Healthy(trust: 0), "Trust").Status);

        var row = RowOf(Healthy(trust: 2), "Trust");
        Assert.Equal(PreflightStatus.Check, row.Status);
        Assert.Equal("2 folders waiting for trust; their runs will not start", row.Text);
    }

    [Fact]
    public void SleepIsOkWhenNeverOrNoRunIsDueAndCheckWhenOneIs()
    {
        Assert.Equal("never sleeps on AC power", RowOf(Healthy(sleep: 0), "Sleep").Text);
        Assert.Equal(PreflightStatus.Ok, RowOf(Healthy(sleep: 0), "Sleep").Status);

        var quietNight = RowOf(Healthy(sleep: 10, runs: [Run("far", Now.AddHours(20))]), "Sleep");
        Assert.Equal(PreflightStatus.Ok, quietNight.Status);
        Assert.Equal("sleeps after 10 min on AC power; no run is due in the next 12 h", quietNight.Text);

        var due = RowOf(Healthy(sleep: 30, runs: [Run("soon", Now.AddHours(6))]), "Sleep");
        Assert.Equal(PreflightStatus.Check, due.Status);
        Assert.Equal("sleeps after 30 min on AC power while a run is due or live", due.Text);

        var live = RowOf(Healthy(sleep: 30, runs: [Run("live")]), "Sleep");
        Assert.Equal(PreflightStatus.Check, live.Status);

        Assert.Equal(PreflightStatus.Unknown, RowOf(Healthy(sleep: null), "Sleep").Status);
    }

    [Fact]
    public void NeedsYouIsCheckWhenSomethingWaits()
    {
        Assert.Equal(PreflightStatus.Ok, RowOf(Healthy(needsYou: 0), "Needs you").Status);

        var row = RowOf(Healthy(needsYou: 1), "Needs you");
        Assert.Equal(PreflightStatus.Check, row.Status);
        Assert.Equal("1 session waiting now", row.Text);
    }

    [Fact]
    public void QuietHoursAreAlwaysOkAndSayWhatIsOn()
    {
        Assert.Equal("off: sound, flash and toasts are on", RowOf(Healthy(), "Quiet hours").Text);

        var now = RowOf(Healthy(quiet: new QuietState("22:00-07:00", true)), "Quiet hours");
        Assert.Equal(PreflightStatus.Ok, now.Status);
        Assert.Equal("22:00-07:00: quiet now, so sound, flash and toasts are off", now.Text);

        var later = RowOf(Healthy(quiet: new QuietState("22:00-07:00", false)), "Quiet hours");
        Assert.Equal("22:00-07:00: not quiet now", later.Text);
    }

    [Fact]
    public void NoRowTextNamesAPathOrAPrompt()
    {
        var report = Preflight.Build(Healthy(needsYou: 1, trust: 1, sleep: 10, runs: [Run("a", Now.AddHours(1))]));

        Assert.All(report.Rows, row =>
        {
            Assert.DoesNotContain("\\", row.Text);
            Assert.DoesNotContain("/", row.Text.Replace("/login", string.Empty));
        });
    }
}
