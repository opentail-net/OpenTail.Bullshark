using OpenTail.Bullshark.Core.Plan;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Plan;

public sealed class PlanFileTests
{
    private const string Plan = """
        # Plan

        Some intro text that is not a batch.

        ## Batch 1 - Scaffold the project
        - [x] Create the solution
        - [x] Add the Core library

        ## Batch 2 - Add the supervisor
        - [x] Write the policy
        - [ ] Write the runner
        - [ ] Add the state file

        ## Batch 3 – Activity panel
        - [ ] Read transcripts
        """;

    [Fact]
    public void ParsesBatchesAndTheirItems()
    {
        var batches = PlanFile.Parse(Plan);

        Assert.Equal(3, batches.Count);
        Assert.Equal(1, batches[0].Number);
        Assert.Equal("Scaffold the project", batches[0].Title);
        Assert.Equal(3, batches[1].Items.Count);
        Assert.Equal("Write the runner", batches[1].Items[1].Text);
        Assert.False(batches[1].Items[1].Done);
        Assert.True(batches[1].Items[0].Done);
    }

    [Fact]
    public void ToleratesAnEnDashInTheHeading()
    {
        Assert.Equal("Activity panel", PlanFile.Parse(Plan)[2].Title);
    }

    [Fact]
    public void NextOpenBatchIsTheFirstBatchWithAnUntickedItem()
    {
        var next = PlanFile.NextOpenBatch(PlanFile.Parse(Plan));

        Assert.NotNull(next);
        Assert.Equal(2, next.Number);
    }

    [Fact]
    public void NoOpenBatchMeansTheWorkIsFinished()
    {
        var done = """
            ## Batch 1 - Only
            - [x] One
            - [X] Two
            """;

        Assert.Null(PlanFile.NextOpenBatch(PlanFile.Parse(done)));
    }

    [Fact]
    public void ItemsBeforeAnyBatchAreIgnored()
    {
        var batches = PlanFile.Parse("- [ ] stray item\n## Batch 1 - A\n- [ ] real item");

        Assert.Single(batches);
        Assert.Equal("real item", Assert.Single(batches[0].Items).Text);
    }

    [Fact]
    public void TheHandoffMessageCarriesTheBatchItself()
    {
        var batch = PlanFile.Parse(Plan)[1];
        var message = PlanFile.HandoffMessage(batch);

        Assert.Contains("Batch 2 - Add the supervisor", message);
        Assert.Contains("- [ ] Write the runner", message);
        Assert.Contains("- [x] Write the policy", message);
        Assert.Contains("SendMessage", message);
    }

    [Fact]
    public void NoFileIsUsedToPassMessages()
    {
        // Owner decision (2026-11-14): sessions talk live, not through files.
        Assert.DoesNotContain("docs/bullshark", PlanFile.HandoffMessage(PlanFile.Parse(Plan)[1]));
    }
}
