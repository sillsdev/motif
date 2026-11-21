using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

public sealed class TrialTouchedReportTests
{
    private static readonly TouchedWords Touched = new(["a", "b"], 502, 3)
    {
        Unexpanded = [new UnexpandedTarget("MoInflAffixSlot", 2)], BaselineWords = 40, AttestedWords = 90,
    };

    [Fact]
    public void ATouchedTrialReturnsItsAddedCappedUnexpandedAndCoverageCountsInTheNextStep()
    {
        var read = ToolOutcome.Ok(new JsonObject(), "Read the report.", "job-1");
        var next = AgentTools.WithTouched(read, Touched).Next!;
        Assert.Contains("added 2 word(s) that use 3 edited object(s)", next);
        Assert.Contains("500 more matched and were not added (cap 500)", next);
        Assert.Contains("matched against 40 Baseline word(s) of 90 attested", next);
        Assert.Contains("MoInflAffixSlot x2", next);
        Assert.Contains("may be incomplete; add --words", next);
        Assert.EndsWith("Read the report.", next);
        Assert.Equal("job-1", AgentTools.WithTouched(read, Touched).JobId);
    }

    [Fact]
    public void ATrialWithoutTouchedOrAReadRefusalIsReturnedAsItWas()
    {
        var read = ToolOutcome.Ok(new JsonObject(), "Read the report.", "job-1");
        Assert.Same(read, AgentTools.WithTouched(read, null));
        var refused = ToolOutcome.Refused("tool.invalid-argument", FailureReason.InvalidArgument, "no");
        Assert.Same(refused, AgentTools.WithTouched(refused, Touched));
    }
}
