using SIL.Motif.App.GuideQueries;
using SIL.Motif.Contract.Query;
using Xunit;
using static SIL.Motif.Tests.App.GuideQueryTestSupport;

namespace SIL.Motif.Tests.App;

public sealed class GuideQueryRunTests
{
    [Fact]
    public void BothSidesReadWithEachWordsMorphCutAndParseState()
    {
        var run = Read(BothAnswer());

        Assert.Equal("both", run.Mode);
        Assert.Equal(["baseline", "proposal"], run.States.Select(state => state.Side));
        var row = run.Rows[0];
        Assert.Equal("ka-onan", row.Baseline!.Analyses[0].Cut);
        Assert.Equal("ka-on-an", row.Proposal!.Analyses[0].Cut);
        Assert.Equal("NOM-eat-IPFV", row.Proposal.Analyses[0].Glosses);
        Assert.Equal("approved", row.Baseline.ParseState);
        Assert.Null(run.Rows[2].Proposal);
    }

    [Fact]
    public void APendingProposalSideIsReadAsPendingWithItsReason()
    {
        var run = Read(PendingAnswer());

        var proposal = run.StateOf("proposal")!;
        Assert.True(proposal.IsPending);
        Assert.Equal("no-trial", proposal.Reason);
        Assert.False(run.StateOf("baseline")!.IsPending);
        Assert.All(run.Rows, row => Assert.Null(row.Proposal));
    }

    [Fact]
    public void AGroupedAnswerReadsItsGroups()
    {
        var json = Response(null, [new QueryGroup(["a", "b"], 4, null, []), new QueryGroup(["c"], 9, null, [])]);

        var run = Read(json);

        Assert.Equal([("a, b", 4), ("c", 9)], run.Groups.Select(group => (group.Key, group.Count)));
    }

    [Fact]
    public void ANewerShapeVersionAndAnAnswerWithoutSidesAreMessagesNotCrashes()
    {
        var older = BaselineAnswer();
        var (newer, problem) = GuideQueryRun.From(older with { Sides = older.Sides! with { Version = 2 } });
        Assert.Null(newer);
        Assert.NotEmpty(problem!);

        var (empty, why) = GuideQueryRun.From(Response(null, []));
        Assert.Null(empty);
        Assert.NotEmpty(why!);
    }

    [Fact]
    public void AnUnknownParseStateIsKeptAsItIs()
    {
        var run = Read(Sides("baseline", "ready", "", "", Row("x", Word("x", "x", "future-state"), null)));

        Assert.Equal("future-state", run.Rows[0].Baseline!.ParseState);
    }
}
