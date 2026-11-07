using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

public sealed class TimingCostPresentationTests
{
    [Fact]
    public void TimingToolUsesTheSamePercentOnlyProjection()
    {
        var response = new TimingResponse("assessment", "all", "kind", 1, 45, 67, [], [], [])
        {
            Work = new WordWorkSummary(1, 765, 765, 765),
            Words = [new TimingWordRow("word", 34, TimingCompletion.Finished) { ParserWork = 765 }],
            CostComparison = new TimingCostComparison(new TimingCostChange(25, null), []),
        };
        var output = ToolOutcome.From(CommandOutcome<TimingResponse>.Success(response)).Value!.ToJsonString();
        Assert.Contains("25", output);
        Assert.DoesNotContain("765", output);
        Assert.DoesNotContain("elapsed", output, StringComparison.OrdinalIgnoreCase);
    }
}
