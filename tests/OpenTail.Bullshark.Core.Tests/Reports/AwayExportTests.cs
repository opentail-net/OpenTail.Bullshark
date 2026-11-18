using System.Globalization;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Reports;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Reports;

/// <summary>
/// "Copy as Markdown" on the Away page (batch B2): the sections and their order, the empty digest, escaping and flattening, the reply cut,
/// the file counts and the order of the rows. The digests are built in memory, as the page's own tests do.
/// </summary>
public sealed class AwayExportTests
{
    private static readonly DateTimeOffset Since = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private static string Local(DateTimeOffset at) => at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static AwayRow Row(string name, DateTimeOffset? last = null, int files = 0, IReadOnlyList<string>? first = null, int failed = 0,
        string? text = null, bool needsYou = false) =>
        new(name, name + "-id", SessionRole.Coder, "working", last, Since.AddDays(-1), files, first ?? [], failed, text, needsYou);

    private static AwayDigest Digest(IReadOnlyList<AwayRow>? rows = null, IReadOnlyList<QuietSession>? quiet = null,
        IReadOnlyList<SupervisorEvent>? events = null, IReadOnlyList<LimitHit>? hits = null) =>
        new(Since, Now, rows ?? [], quiet ?? [], events ?? [], hits ?? []);

    private static string Markdown(AwayDigest digest) => AwayExport.Markdown(digest, Since, Now);

    [Fact]
    public void AFullDigestHasEachSectionInThePagesOrder()
    {
        var digest = Digest(
            rows: [Row("alpha", Since.AddMinutes(30), files: 2, first: ["a.cs", "b.cs"], failed: 1, text: "Done.", needsYou: true)],
            quiet: [new QuietSession("beta", NeedsYou: false)],
            events: [new SupervisorEvent(Since.AddMinutes(5), "alpha", "restart", "after a crash")],
            hits: [new LimitHit(Since.AddMinutes(20), "five_hour", Since.AddMinutes(20).AddHours(5), TokenUsage.Zero)]);

        var text = Markdown(digest);

        Assert.StartsWith($"# While you were away\n\nFrom {Local(Since)} to {Local(Now)}.\n\n", text);
        var sessions = text.IndexOf("## Sessions, newest first", StringComparison.Ordinal);
        var events = text.IndexOf("## Supervisor events, oldest first", StringComparison.Ordinal);
        var limits = text.IndexOf("## Limits reached, oldest first", StringComparison.Ordinal);
        var quiet = text.IndexOf("## Quiet sessions", StringComparison.Ordinal);
        Assert.True(0 < sessions && sessions < events && events < limits && limits < quiet);
        Assert.Contains($"- **alpha** (coder, working, needs you now): last activity {Local(Since.AddMinutes(30))}; " +
                        "2 files written in the whole session: a.cs, b.cs; 1 failed tool call. Last reply: \"Done.\"", text);
        Assert.Contains($"- {Local(Since.AddMinutes(5))} · alpha · restart · after a crash", text);
        Assert.Contains($"- {Local(Since.AddMinutes(20))} 5-hour limit, resets {Local(Since.AddMinutes(20).AddHours(5))}", text);
        Assert.EndsWith("- beta\n", text);
        Assert.DoesNotContain("Nothing happened", text);
    }

    [Fact]
    public void AnEmptyDigestSaysNothingHappenedAndEndsWithOneNewline()
    {
        var text = Markdown(Digest());

        Assert.Equal($"# While you were away\n\nFrom {Local(Since)} to {Local(Now)}.\n\nNothing happened in this period.\n", text);
    }

    [Fact]
    public void EmptySectionsAreLeftOut()
    {
        var text = Markdown(Digest(events: [new SupervisorEvent(Since.AddMinutes(5), "alpha", "alert", "")]));

        Assert.Contains("## Supervisor events, oldest first", text);
        Assert.DoesNotContain("## Sessions", text);
        Assert.DoesNotContain("## Limits", text);
        Assert.DoesNotContain("## Quiet", text);
        Assert.DoesNotContain("Nothing happened", text);
    }

    [Fact]
    public void AQuietOnlyDigestNamesTheQuietSessionsAndSaysNothingHappened()
    {
        var text = Markdown(Digest(quiet: [new QuietSession("beta", NeedsYou: true), new QuietSession("alpha", NeedsYou: false)]));

        Assert.Contains("Nothing happened in this period.", text);
        Assert.Contains("## Quiet sessions\n\n- beta (needs you)\n- alpha\n", text);
    }

    [Fact]
    public void ASessionWithNoTranscriptOrFilesSaysSo()
    {
        var text = Markdown(Digest(rows: [Row("alpha")]));

        Assert.Contains("- **alpha** (coder, working): no transcript yet; no files written; 0 failed tool calls. No reply text in the transcript.", text);
    }

    [Fact]
    public void TheFilesNamedAreTheFirstFiveAndTheCountIsTheWholeSession()
    {
        var first = new[] { "a.cs", "b.cs", "c.cs", "d.cs", "e.cs" };

        var text = Markdown(Digest(rows: [Row("alpha", Since.AddMinutes(1), files: 7, first: first)]));

        Assert.Contains("7 files written in the whole session: a.cs, b.cs, c.cs, d.cs, e.cs and 2 more;", text);
    }

    [Fact]
    public void MarkdownCharactersInNamesAndTextShowAsTyped()
    {
        var row = Row("a|b`c*d_e", Since.AddMinutes(1), files: 1, first: ["x|y.md"], text: "use `code` and [link](url) <b>tag</b>");

        var text = Markdown(Digest(rows: [row]));

        Assert.Contains("**a\\|b\\`c\\*d\\_e**", text);
        Assert.Contains("x\\|y.md", text);
        Assert.Contains("use \\`code\\` and \\[link\\](url) \\<b\\>tag\\</b\\>", text);
    }

    [Fact]
    public void NewlinesAreFlattenedAndTheReplyIsCutTo300Characters()
    {
        var longText = "line one\r\nline two\n" + new string('x', 1000);

        var text = Markdown(Digest(rows: [Row("alpha", Since.AddMinutes(1), text: longText)]));

        Assert.DoesNotContain("\r", text);
        var line = text.Split('\n').Single(candidate => candidate.StartsWith("- **alpha**", StringComparison.Ordinal));
        const string Lead = "Last reply: \"";
        var reply = line[(line.IndexOf(Lead, StringComparison.Ordinal) + Lead.Length)..^2];
        Assert.StartsWith("line one line two xxx", reply);
        Assert.EndsWith("…", reply);
        Assert.Equal(AwayDigest.TextLimit, reply.Length);
    }

    [Fact]
    public void RowsAndEventsKeepTheOrderTheDigestGivesThem()
    {
        var text = Markdown(Digest(
            rows: [Row("second", Since.AddMinutes(50)), Row("first", Since.AddMinutes(10))],
            events: [new SupervisorEvent(Since.AddMinutes(40), "second", "restart", "late"), new SupervisorEvent(Since.AddMinutes(9), "first", "alert", "early")]));

        Assert.True(text.IndexOf("**second**", StringComparison.Ordinal) < text.IndexOf("**first**", StringComparison.Ordinal));
        Assert.True(text.IndexOf("· late", StringComparison.Ordinal) < text.IndexOf("· early", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEventWithNoSessionOrDetailIsItsTimeAndKind()
    {
        var at = Since.AddMinutes(5);

        var text = Markdown(Digest(events: [new SupervisorEvent(at, "", "alert", "")]));

        Assert.Contains($"- {Local(at)} · alert\n", text);
    }
}
