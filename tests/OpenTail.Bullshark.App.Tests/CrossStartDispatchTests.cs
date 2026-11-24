using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Setup;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The dispatch of a start in another platform (resume source and target plan, S3b-B1): the route it takes, its refusals, its tab title and its status
/// line. Pure: no window is made and no process starts. The window's start methods are not called here; they are checked at S5 (the owner's screen
/// check). The expected routes are written out in <see cref="Cases"/>, not computed from the rules.
/// </summary>
public sealed class CrossStartDispatchTests
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
    /// The 54 combinations of source, target, mode (Full, LastTurns, Summary: the three values of <see cref="ContinueMode"/>) and the Copilot constant
    /// (false, then true), each with its expected route. A row with the own platform as target, a mode other than Summary, or a Copilot target is
    /// refused; a Claude or a Codex target from a summary is started in that platform.
    /// </summary>
    public static IEnumerable<object[]> Cases() => new[]
    {
        // Source Claude, target Claude: the own platform is never a cross-platform start.
        new object[] { Platform.Claude, Platform.Claude, ContinueMode.Full, false, CrossStartKind.Refused },
        new object[] { Platform.Claude, Platform.Claude, ContinueMode.Full, true, CrossStartKind.Refused },
        new object[] { Platform.Claude, Platform.Claude, ContinueMode.LastTurns, false, CrossStartKind.Refused },
        new object[] { Platform.Claude, Platform.Claude, ContinueMode.LastTurns, true, CrossStartKind.Refused },
        new object[] { Platform.Claude, Platform.Claude, ContinueMode.Summary, false, CrossStartKind.Refused },
        new object[] { Platform.Claude, Platform.Claude, ContinueMode.Summary, true, CrossStartKind.Refused },

        // Source Claude, target Codex: only a summary starts in Codex.
        new object[] { Platform.Claude, Platform.Codex, ContinueMode.Full, false, CrossStartKind.Refused },
        new object[] { Platform.Claude, Platform.Codex, ContinueMode.Full, true, CrossStartKind.Refused },
        new object[] { Platform.Claude, Platform.Codex, ContinueMode.LastTurns, false, CrossStartKind.Refused },
        new object[] { Platform.Claude, Platform.Codex, ContinueMode.LastTurns, true, CrossStartKind.Refused },
        new object[] { Platform.Claude, Platform.Codex, ContinueMode.Summary, false, CrossStartKind.Codex },
        new object[] { Platform.Claude, Platform.Codex, ContinueMode.Summary, true, CrossStartKind.Codex },

        // Source Claude, target Copilot: refused in every mode (RS-D8).
        new object[] { Platform.Claude, Platform.Copilot, ContinueMode.Full, false, CrossStartKind.Refused },
        new object[] { Platform.Claude, Platform.Copilot, ContinueMode.Full, true, CrossStartKind.Refused },
        new object[] { Platform.Claude, Platform.Copilot, ContinueMode.LastTurns, false, CrossStartKind.Refused },
        new object[] { Platform.Claude, Platform.Copilot, ContinueMode.LastTurns, true, CrossStartKind.Refused },
        new object[] { Platform.Claude, Platform.Copilot, ContinueMode.Summary, false, CrossStartKind.Refused },
        new object[] { Platform.Claude, Platform.Copilot, ContinueMode.Summary, true, CrossStartKind.Refused },

        // Source Codex, target Claude: only a summary starts in Claude.
        new object[] { Platform.Codex, Platform.Claude, ContinueMode.Full, false, CrossStartKind.Refused },
        new object[] { Platform.Codex, Platform.Claude, ContinueMode.Full, true, CrossStartKind.Refused },
        new object[] { Platform.Codex, Platform.Claude, ContinueMode.LastTurns, false, CrossStartKind.Refused },
        new object[] { Platform.Codex, Platform.Claude, ContinueMode.LastTurns, true, CrossStartKind.Refused },
        new object[] { Platform.Codex, Platform.Claude, ContinueMode.Summary, false, CrossStartKind.Claude },
        new object[] { Platform.Codex, Platform.Claude, ContinueMode.Summary, true, CrossStartKind.Claude },

        // Source Codex, target Codex: the own platform.
        new object[] { Platform.Codex, Platform.Codex, ContinueMode.Full, false, CrossStartKind.Refused },
        new object[] { Platform.Codex, Platform.Codex, ContinueMode.Full, true, CrossStartKind.Refused },
        new object[] { Platform.Codex, Platform.Codex, ContinueMode.LastTurns, false, CrossStartKind.Refused },
        new object[] { Platform.Codex, Platform.Codex, ContinueMode.LastTurns, true, CrossStartKind.Refused },
        new object[] { Platform.Codex, Platform.Codex, ContinueMode.Summary, false, CrossStartKind.Refused },
        new object[] { Platform.Codex, Platform.Codex, ContinueMode.Summary, true, CrossStartKind.Refused },

        // Source Codex, target Copilot: refused in every mode (RS-D8).
        new object[] { Platform.Codex, Platform.Copilot, ContinueMode.Full, false, CrossStartKind.Refused },
        new object[] { Platform.Codex, Platform.Copilot, ContinueMode.Full, true, CrossStartKind.Refused },
        new object[] { Platform.Codex, Platform.Copilot, ContinueMode.LastTurns, false, CrossStartKind.Refused },
        new object[] { Platform.Codex, Platform.Copilot, ContinueMode.LastTurns, true, CrossStartKind.Refused },
        new object[] { Platform.Codex, Platform.Copilot, ContinueMode.Summary, false, CrossStartKind.Refused },
        new object[] { Platform.Codex, Platform.Copilot, ContinueMode.Summary, true, CrossStartKind.Refused },

        // Source Copilot, target Claude: only a summary starts in Claude.
        new object[] { Platform.Copilot, Platform.Claude, ContinueMode.Full, false, CrossStartKind.Refused },
        new object[] { Platform.Copilot, Platform.Claude, ContinueMode.Full, true, CrossStartKind.Refused },
        new object[] { Platform.Copilot, Platform.Claude, ContinueMode.LastTurns, false, CrossStartKind.Refused },
        new object[] { Platform.Copilot, Platform.Claude, ContinueMode.LastTurns, true, CrossStartKind.Refused },
        new object[] { Platform.Copilot, Platform.Claude, ContinueMode.Summary, false, CrossStartKind.Claude },
        new object[] { Platform.Copilot, Platform.Claude, ContinueMode.Summary, true, CrossStartKind.Claude },

        // Source Copilot, target Codex: only a summary starts in Codex.
        new object[] { Platform.Copilot, Platform.Codex, ContinueMode.Full, false, CrossStartKind.Refused },
        new object[] { Platform.Copilot, Platform.Codex, ContinueMode.Full, true, CrossStartKind.Refused },
        new object[] { Platform.Copilot, Platform.Codex, ContinueMode.LastTurns, false, CrossStartKind.Refused },
        new object[] { Platform.Copilot, Platform.Codex, ContinueMode.LastTurns, true, CrossStartKind.Refused },
        new object[] { Platform.Copilot, Platform.Codex, ContinueMode.Summary, false, CrossStartKind.Codex },
        new object[] { Platform.Copilot, Platform.Codex, ContinueMode.Summary, true, CrossStartKind.Codex },

        // Source Copilot, target Copilot: the own platform.
        new object[] { Platform.Copilot, Platform.Copilot, ContinueMode.Full, false, CrossStartKind.Refused },
        new object[] { Platform.Copilot, Platform.Copilot, ContinueMode.Full, true, CrossStartKind.Refused },
        new object[] { Platform.Copilot, Platform.Copilot, ContinueMode.LastTurns, false, CrossStartKind.Refused },
        new object[] { Platform.Copilot, Platform.Copilot, ContinueMode.LastTurns, true, CrossStartKind.Refused },
        new object[] { Platform.Copilot, Platform.Copilot, ContinueMode.Summary, false, CrossStartKind.Refused },
        new object[] { Platform.Copilot, Platform.Copilot, ContinueMode.Summary, true, CrossStartKind.Refused },
    };

    [Theory]
    [InlineData(Platform.Claude, false)]
    [InlineData(Platform.Claude, true)]
    [InlineData(Platform.Codex, false)]
    [InlineData(Platform.Codex, true)]
    [InlineData(Platform.Copilot, false)]
    [InlineData(Platform.Copilot, true)]
    public void AnOpenCodeTargetFromASummaryRoutesToOpenCodeWhateverTheCopilotSwitch(Platform source, bool copilotEnabled)
    {
        var route = CrossStartRoute.For(Of(source), ProviderId.OpenCode, ContinueMode.Summary, copilotEnabled);
        Assert.Equal(CrossStartKind.OpenCode, route.Kind);
        Assert.Null(route.Refusal);
    }

    [Theory]
    [InlineData(ContinueMode.Full)]
    [InlineData(ContinueMode.LastTurns)]
    public void AnOpenCodeTargetOutsideASummaryIsRefusedWithItsReason(ContinueMode mode)
    {
        var route = CrossStartRoute.For(ProviderId.Claude, ProviderId.OpenCode, mode);
        Assert.Equal(CrossStartKind.Refused, route.Kind);
        Assert.Equal("Only a summary can start in another platform. A full resume stays in Claude.", route.Refusal);
    }

    [Fact]
    public void TheOpenCodeTabAndTheStartedLineNameOpenCode()
    {
        Assert.Equal("OpenCode: summary of An example session", CrossStartRoute.TabTitleOf(ProviderId.OpenCode, "An example session"));
        Assert.Equal("Started OpenCode from a summary of the Claude session.", CrossStartRoute.StartedLine(ProviderId.Claude, ProviderId.OpenCode));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TheRouteMatchesTheTable(Platform source, Platform target, ContinueMode mode, bool copilotEnabled, CrossStartKind expected)
    {
        var route = CrossStartRoute.For(Of(source), Of(target), mode, copilotEnabled);
        Assert.Equal(expected, route.Kind);
        Assert.Equal(expected == CrossStartKind.Refused, route.Refusal is not null);
    }

    [Fact]
    public void ACrossStartInAModeOtherThanSummaryIsRefusedWithTheModeReason()
    {
        var route = CrossStartRoute.For(ProviderId.Claude, ProviderId.Codex, ContinueMode.LastTurns);
        Assert.Equal(CrossStartKind.Refused, route.Kind);
        Assert.Equal(ResumeTarget.RefusalReason(ProviderId.Claude, ContinueMode.LastTurns, ProviderId.Codex), route.Refusal);
        Assert.StartsWith("Only a summary can start in another platform.", route.Refusal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACopilotTargetIsRefusedWhateverTheConstantSays(bool copilotEnabled)
    {
        var route = CrossStartRoute.For(ProviderId.Claude, ProviderId.Copilot, ContinueMode.Summary, copilotEnabled);
        Assert.Equal(CrossStartKind.Refused, route.Kind);
        Assert.Equal(
            "Copilot cannot be the target yet: the Copilot install that runs is not confirmed (G-O5).",
            route.Refusal);
    }

    [Fact]
    public void WhenBothRulesFailTheModeReasonComesFirst()
    {
        var route = CrossStartRoute.For(ProviderId.Codex, ProviderId.Copilot, ContinueMode.Full, copilotEnabled: false);
        Assert.Equal(ResumeTarget.RefusalReason(ProviderId.Codex, ContinueMode.Full, ProviderId.Copilot, copilotEnabled: false), route.Refusal);
        Assert.StartsWith("Only a summary can start in another platform.", route.Refusal);
    }

    [Fact]
    public void TheOwnPlatformIsRefusedAsNotACrossStart()
    {
        var route = CrossStartRoute.For(ProviderId.Codex, ProviderId.Codex, ContinueMode.Summary);
        Assert.Equal(CrossStartKind.Refused, route.Kind);
        Assert.StartsWith("Not started: the target is the session's own platform", route.Refusal);
    }

    [Fact]
    public void TheCodexTabTitleOfAClaudeSourceIsCodexSummaryOfTheSourceTitle()
    {
        Assert.Equal("Codex: summary of Plan the release", CrossStartRoute.TabTitleOf(ProviderId.Codex, "Plan the release"));
    }

    [Theory]
    [InlineData(Platform.Codex, "Codex: summary of Note")]
    [InlineData(Platform.Claude, "Claude: summary of Note")]
    public void TheTabTitleNamesTheTargetAndTheSourceTitle(Platform target, string expected)
    {
        Assert.Equal(expected, CrossStartRoute.TabTitleOf(Of(target), "Note"));
    }

    [Fact]
    public void TheStatusLineNamesTheTargetAndTheSource()
    {
        Assert.Equal(
            "Started Codex from a summary of the Claude session.",
            CrossStartRoute.StartedLine(ProviderId.Claude, ProviderId.Codex));
        Assert.Equal(
            "Started Claude from a summary of the Copilot session.",
            CrossStartRoute.StartedLine(ProviderId.Copilot, ProviderId.Claude));
    }

    [Fact]
    public void TheCrossClaudeRequestIsTheSoloPatternWithTheTextAndTheFolderPassedIn()
    {
        var request = SessionWindow.CrossClaudeRequestOf(@"C:\work\repo", "Carry on from here.", PatternStore.Defaults("solo"));
        Assert.Equal("solo", request.PatternKey);
        Assert.Equal("Carry on from here.", request.Task);
        Assert.Equal(@"C:\work\repo", request.Folder);
        Assert.Equal(1, request.Coders);
    }

    [Fact]
    public void TheCrossClaudeRequestTakesTheClaudeScreensPlannerAndTheSavedCoder()
    {
        // The planner is the Claude screen's default model and effort (RS-D12, ClaudeDefaults of the saved solo); the coder is the saved solo's.
        var solo = PatternStore.Defaults("solo") with
        {
            PlannerModel = "saved-planner",
            PlannerEffort = "saved-planner-effort",
            CoderModel = "saved-coder",
            CoderEffort = "saved-coder-effort",
        };
        var request = SessionWindow.CrossClaudeRequestOf("C:\\work", "text", solo);
        Assert.Equal(CrossPlatformStartText.ClaudeDefaults(solo), (request.PlannerModel, request.PlannerEffort));
        Assert.Equal(("saved-planner", "saved-planner-effort"), (request.PlannerModel, request.PlannerEffort));
        Assert.Equal(("saved-coder", "saved-coder-effort"), (request.CoderModel, request.CoderEffort));
    }
}
