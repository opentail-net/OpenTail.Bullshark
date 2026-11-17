using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Claude;

/// <summary>
/// Round 7 item 3: the optional `status` and `waitingFor` fields of `claude agents --json`. The docs name them; 2.1.326 does not print them
/// (checked 2026-11-17 on the owner's listing). A listing with them and one without must both read, and an unknown field must not break it.
/// </summary>
public sealed class ListingFieldsTests
{
    // Shape of 2.1.326 (captured 2026-11-17, with names and ids replaced): no status, no waitingFor.
    private const string Current = """
        [
          { "id": "0b6cfaa9", "cwd": "C:\\Git\\X", "kind": "background", "startedAt": 1784881559887,
            "sessionId": "0b6cfaa9-ba37-4d3b-8b79-59642061b70f", "name": "Review", "state": "blocked" },
          { "pid": 15732, "cwd": "C:\\Git", "kind": "interactive", "startedAt": 1794672408677,
            "sessionId": "c71e8ad0-6308-41be-b119-46235935a059", "name": "git-86", "status": "busy" }
        ]
        """;

    // Shape the docs describe (hand-built): a waiting session with its reason as text, and an object form that a later version may use.
    private const string Newer = """
        [
          { "id": "0b6cfaa9", "cwd": "C:\\Git\\X", "kind": "background", "startedAt": 1784881559887,
            "sessionId": "0b6cfaa9-ba37-4d3b-8b79-59642061b70f", "name": "Review", "state": "blocked",
            "status": "waiting", "waitingFor": "permission prompt", "goal": { "turns": 3 } },
          { "pid": 15732, "cwd": "C:\\Git", "kind": "interactive", "startedAt": 1794672408677,
            "sessionId": "c71e8ad0-6308-41be-b119-46235935a059", "name": "git-86", "status": "waiting",
            "waitingFor": { "kind": "dialog", "title": "Trust this folder?" } },
          { "id": "ffe9bf2f", "kind": "background", "sessionId": "ffe9bf2f-97af-46cd-b6aa-ade7c397d281",
            "name": "Blank", "state": "blocked", "waitingFor": "   " },
          { "id": "94d6c2c4", "kind": "background", "sessionId": "94d6c2c4-8925-4825-9399-8ad1b070628f",
            "name": "Count", "state": "blocked", "waitingFor": 3 }
        ]
        """;

    [Fact]
    public void AListingWithoutTheFieldsReadsAsItDid()
    {
        var agents = AgentInfo.ParseList(Current);

        Assert.All(agents, agent =>
        {
            Assert.Null(agent.WaitingFor);
            Assert.Null(agent.Waiting);
        });
        Assert.Equal("blocked", agents[0].EffectiveState);
        Assert.Equal("busy", agents[1].EffectiveState);
        Assert.Null(agents[0].Status);
    }

    [Fact]
    public void AWaitingReasonAsTextIsKept()
    {
        var agents = AgentInfo.ParseList(Newer);

        Assert.Equal("permission prompt", agents[0].WaitingFor);
        Assert.Equal("permission prompt", agents[0].Waiting);
        Assert.Equal("waiting", agents[0].Status);
        // The state the supervisor acts on is still the background `state`.
        Assert.Equal("blocked", agents[0].EffectiveState);
    }

    [Fact]
    public void AWaitingReasonAsAnObjectIsItsRawJson()
    {
        var agents = AgentInfo.ParseList(Newer);

        Assert.Equal("{ \"kind\": \"dialog\", \"title\": \"Trust this folder?\" }", agents[1].WaitingFor);
        Assert.Equal("waiting", agents[1].Status);
        Assert.Equal("busy", AgentInfo.ParseList(Current)[1].EffectiveState);
    }

    [Fact]
    public void ABlankOrNonTextReasonMeansNoReason()
    {
        var agents = AgentInfo.ParseList(Newer);

        Assert.Null(agents[2].Waiting);
        Assert.Null(agents[3].WaitingFor);
        Assert.Null(agents[3].Waiting);
    }

    [Fact]
    public void AnUnknownFieldDoesNotStopTheListing()
    {
        // The `goal` object on the first entry is not read, and the entries after it still are.
        var agents = AgentInfo.ParseList(Newer);

        Assert.Equal(4, agents.Count);
        Assert.Equal("c71e8ad0-6308-41be-b119-46235935a059", agents[1].SessionId);
    }

    [Fact]
    public void TheAttentionDetailSaysWhatTheSessionWaitsFor()
    {
        var start = new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
        var record = new SessionRecord(SessionRole.Coder, "0b6cfaa9-ba37-4d3b-8b79-59642061b70f", "Review", start, 0, [], null, null);

        var waiting = Assert.Single(AttentionList.From([record], AgentInfo.ParseList(Newer)));
        Assert.Equal(AttentionReason.Blocked, waiting.Reason);
        Assert.Equal("waiting for permission prompt", waiting.Detail);

        var unannotated = Assert.Single(AttentionList.From([record], AgentInfo.ParseList(Current)));
        Assert.Equal("waiting for input", unannotated.Detail);
    }
}
