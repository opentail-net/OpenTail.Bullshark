using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The resume target rules (the resume source and target plan, stage S1): which platform a resume may start in, the refusal texts, the default target
/// and the labels. Pure: no window is made. The expected answers are written out in <see cref="Cases"/>, not computed from the rules.
/// </summary>
public sealed class ResumeTargetTests
{
    /// <summary>The three platforms, as the test table names them. <see cref="Of"/> maps each to its provider id.</summary>
    public enum Platform
    {
        Claude,
        Codex,
        Copilot,
    }

    private static ProviderId Of(Platform platform) => platform switch
    {
        Platform.Claude => ProviderId.Claude,
        Platform.Codex => ProviderId.Codex,
        Platform.Copilot => ProviderId.Copilot,
        _ => throw new ArgumentOutOfRangeException(nameof(platform)),
    };

    /// <summary>
    /// The 27 combinations of source, mode and target, each with its expected answer: the fourth value with Copilot disabled (the constant), the fifth
    /// with Copilot enabled.
    /// </summary>
    public static IEnumerable<object[]> Cases() => new[]
    {
        // Source Claude.
        new object[] { Platform.Claude, ContinueMode.Full, Platform.Claude, true, true },
        new object[] { Platform.Claude, ContinueMode.Full, Platform.Codex, false, false },
        new object[] { Platform.Claude, ContinueMode.Full, Platform.Copilot, false, false },
        new object[] { Platform.Claude, ContinueMode.LastTurns, Platform.Claude, true, true },
        new object[] { Platform.Claude, ContinueMode.LastTurns, Platform.Codex, false, false },
        new object[] { Platform.Claude, ContinueMode.LastTurns, Platform.Copilot, false, false },
        new object[] { Platform.Claude, ContinueMode.Summary, Platform.Claude, true, true },
        new object[] { Platform.Claude, ContinueMode.Summary, Platform.Codex, true, true },
        new object[] { Platform.Claude, ContinueMode.Summary, Platform.Copilot, false, true },

        // Source Codex.
        new object[] { Platform.Codex, ContinueMode.Full, Platform.Claude, false, false },
        new object[] { Platform.Codex, ContinueMode.Full, Platform.Codex, true, true },
        new object[] { Platform.Codex, ContinueMode.Full, Platform.Copilot, false, false },
        new object[] { Platform.Codex, ContinueMode.LastTurns, Platform.Claude, false, false },
        new object[] { Platform.Codex, ContinueMode.LastTurns, Platform.Codex, true, true },
        new object[] { Platform.Codex, ContinueMode.LastTurns, Platform.Copilot, false, false },
        new object[] { Platform.Codex, ContinueMode.Summary, Platform.Claude, true, true },
        new object[] { Platform.Codex, ContinueMode.Summary, Platform.Codex, true, true },
        new object[] { Platform.Codex, ContinueMode.Summary, Platform.Copilot, false, true },

        // Source Copilot. Its own platform stays allowed while the Copilot target is disabled (RS-D1).
        new object[] { Platform.Copilot, ContinueMode.Full, Platform.Claude, false, false },
        new object[] { Platform.Copilot, ContinueMode.Full, Platform.Codex, false, false },
        new object[] { Platform.Copilot, ContinueMode.Full, Platform.Copilot, true, true },
        new object[] { Platform.Copilot, ContinueMode.LastTurns, Platform.Claude, false, false },
        new object[] { Platform.Copilot, ContinueMode.LastTurns, Platform.Codex, false, false },
        new object[] { Platform.Copilot, ContinueMode.LastTurns, Platform.Copilot, true, true },
        new object[] { Platform.Copilot, ContinueMode.Summary, Platform.Claude, true, true },
        new object[] { Platform.Copilot, ContinueMode.Summary, Platform.Codex, true, true },
        new object[] { Platform.Copilot, ContinueMode.Summary, Platform.Copilot, true, true },
    };

    /// <summary>The table with the Copilot-disabled answer only.</summary>
    public static IEnumerable<object[]> DisabledCases() => Cases().Select(row => new object[] { row[0], row[1], row[2], row[3] });

    /// <summary>The table with the Copilot-enabled answer only.</summary>
    public static IEnumerable<object[]> EnabledCases() => Cases().Select(row => new object[] { row[0], row[1], row[2], row[4] });

    [Theory]
    [MemberData(nameof(DisabledCases))]
    public void AllowedWithCopilotDisabledMatchesTheTable(Platform source, ContinueMode mode, Platform target, bool expected)
    {
        Assert.Equal(expected, ResumeTarget.Allowed(Of(source), mode, Of(target), copilotEnabled: false));
    }

    [Theory]
    [MemberData(nameof(EnabledCases))]
    public void AllowedWithCopilotEnabledMatchesTheTable(Platform source, ContinueMode mode, Platform target, bool expected)
    {
        Assert.Equal(expected, ResumeTarget.Allowed(Of(source), mode, Of(target), copilotEnabled: true));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void RefusalReasonIsNullExactlyWhenAllowed(
        Platform source, ContinueMode mode, Platform target, bool expectedWhenDisabled, bool expectedWhenEnabled)
    {
        Assert.Equal(expectedWhenDisabled, ResumeTarget.RefusalReason(Of(source), mode, Of(target), copilotEnabled: false) is null);
        Assert.Equal(expectedWhenEnabled, ResumeTarget.RefusalReason(Of(source), mode, Of(target), copilotEnabled: true) is null);
    }

    [Fact]
    public void WithTheFlagOnTheConstantCopilotIsATargetFromASummaryOnly()
    {
        // PP-D2 (2026-11-23): Copilot is an enabled target. From a summary it is allowed; a full or last-turns start stays in the source platform.
        Assert.True(ResumeTarget.Allowed(ProviderId.Claude, ContinueMode.Summary, ProviderId.Copilot));
        Assert.Null(ResumeTarget.RefusalReason(ProviderId.Claude, ContinueMode.Summary, ProviderId.Copilot));
        Assert.False(ResumeTarget.Allowed(ProviderId.Claude, ContinueMode.Full, ProviderId.Copilot));
        Assert.True(ResumeTarget.Allowed(ProviderId.Claude, ContinueMode.Summary, ProviderId.Codex));
    }

    [Fact]
    public void ACrossPlatformFullOrLastTurnsStartIsRefusedWithItsExactReason()
    {
        Assert.Equal(
            "Only a summary can start in another platform. A full or last-turns resume stays in Claude.",
            ResumeTarget.RefusalReason(ProviderId.Claude, ContinueMode.Full, ProviderId.Codex));
        Assert.Equal(
            "Only a summary can start in another platform. A full or last-turns resume stays in Copilot.",
            ResumeTarget.RefusalReason(ProviderId.Copilot, ContinueMode.LastTurns, ProviderId.Claude));
    }

    [Fact]
    public void ACopilotTargetFromASummaryIsRefusedWithItsExactReasonWhileDisabled()
    {
        Assert.Equal(
            "Copilot cannot be the target yet: the Copilot install that runs is not confirmed (G-O5).",
            ResumeTarget.RefusalReason(ProviderId.Claude, ContinueMode.Summary, ProviderId.Copilot, copilotEnabled: false));
    }

    [Fact]
    public void WhenBothRulesFailTheModeReasonComesFirst()
    {
        Assert.Equal(
            "Only a summary can start in another platform. A full or last-turns resume stays in Claude.",
            ResumeTarget.RefusalReason(ProviderId.Claude, ContinueMode.Full, ProviderId.Copilot, copilotEnabled: false));
    }

    [Fact]
    public void TheDefaultTargetIsTheSourceForEachProvider()
    {
        Assert.Equal(ProviderId.Claude, ResumeTarget.DefaultTarget(ProviderId.Claude));
        Assert.Equal(ProviderId.Codex, ResumeTarget.DefaultTarget(ProviderId.Codex));
        Assert.Equal(ProviderId.Copilot, ResumeTarget.DefaultTarget(ProviderId.Copilot));
    }

    [Fact]
    public void TheTitleNamesTheSourceAndTheButtonNamesTheTarget()
    {
        Assert.Equal("Resume a Codex session", ResumeTarget.TitleOf(ProviderId.Codex));
        Assert.Equal("Resume a Claude session", ResumeTarget.TitleOf(ProviderId.Claude));
        Assert.Equal("Start in Claude", ResumeTarget.ButtonOf(ProviderId.Claude));
        Assert.Equal("Start in Copilot", ResumeTarget.ButtonOf(ProviderId.Copilot));
    }

    [Fact]
    public void TheSummaryLineIsShownOnlyForACrossPlatformStart()
    {
        Assert.Equal("Summary of Claude session → new Codex session", ResumeTarget.SummaryLine(ProviderId.Claude, ProviderId.Codex));
        Assert.Equal("Summary of Copilot session → new Claude session", ResumeTarget.SummaryLine(ProviderId.Copilot, ProviderId.Claude));
        Assert.Null(ResumeTarget.SummaryLine(ProviderId.Claude, ProviderId.Claude));
        Assert.Null(ResumeTarget.SummaryLine(ProviderId.Copilot, ProviderId.Copilot));
    }

    [Fact]
    public void TheCopilotTargetIsEnabledByTheConstant()
    {
        // PP-D2 (2026-11-23): the owner's direction is Copilot 1.0.104-3, so Copilot is a target.
        Assert.True(ResumeTarget.CopilotTargetEnabled);
    }
}
