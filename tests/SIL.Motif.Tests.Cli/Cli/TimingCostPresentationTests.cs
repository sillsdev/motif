using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class TimingCostPresentationTests
{
    [Fact]
    public void NoLocalMillisecondsOrRawWorkInTimingResults()
    {
        var response = new TimingResponse("assessment", "all", "rule", 1, 23, 45,
            [new SlowWordTiming("word", 34) { ParserWork = 765 }], [], [])
        {
            Work = new WordWorkSummary(1, 765, 765, 765),
            Words = [new TimingWordRow("word", 34, TimingCompletion.Finished) { ParserWork = 765 }],
            CostComparison = new TimingCostComparison(new TimingCostChange(25, null),
                [new TimingWordCostChange("word", new TimingCostChange(25, null))]),
        };
        var json = CommandTextRenderer.Render(CommandOutcome<TimingResponse>.Success(response), true).Output;
        var text = CommandTextRenderer.Render(CommandOutcome<TimingResponse>.Success(response), false).Output;
        Assert.Contains("25", text);
        foreach (var output in new[] { json, text, TimingPresentation.Timing(response).ToJsonString() })
        {
            Assert.DoesNotContain("765", output);
            Assert.DoesNotContain("elapsed", output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(" ms", output, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void OverviewDoesNotPublishLocalTimeOrRawWork()
    {
        var timing = new OverviewTiming(45, 67, [new SlowWordTiming("word", 34) { ParserWork = 765 }], 0)
        { Work = new WordWorkSummary(1, 765, 765, 765) };
        var response = new OverviewResponse("project", DateTimeOffset.UtcNow, null, 1, 0, 1, 0, 1, 1, 1,
            "assessment", null, 88, null, null, null!, null!, timing, null);
        var json = TimingPresentation.Overview(response).ToJsonString();
        Assert.DoesNotContain("765", json);
        Assert.DoesNotContain("elapsed", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TimeMs", json, StringComparison.OrdinalIgnoreCase);
    }
}
