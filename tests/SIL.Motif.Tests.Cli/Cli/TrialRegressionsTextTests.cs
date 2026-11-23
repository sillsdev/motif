using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Review;
using Xunit;

namespace SIL.Motif.Tests.Cli.Cli;

public sealed class TrialRegressionsTextTests
{
    [Fact]
    public void ARegressionGroupNamesWordsWithBeforeAndAfterAndSaysHowToSeeTheRest()
    {
        var words = Enumerable.Range(0, 2).Select(i => new RegressedWord("w" + i, "case-" + i, 2, 0,
            ["kwish (go) + isha (CAUS)", "kwish (go) + ish (CAUS)"], [], [], [], "motif parse w --trace --at baseline",
            "motif parse w --trace --at p", i == 0 ? [new TouchedByOperation(4, "op-4", "lexical/lexEntry/createAlternateForms",
                ["form-1"], "overlap")] : [])).ToArray();
        var regressions = new TrialRegressions([new RegressionGroup("new-word-failures", 160, 158, words,
            "motif proposal trial --project <fwdata> p --detail full --section wordforms --flag new-word-failures --limit 200")]);
        var text = TrialReviewTextRenderer.RenderRegressions(regressions);
        Assert.Contains("Regressed words, new-word-failures: 160: ", text);
        Assert.Contains("w0 (kwish (go) + isha (CAUS) | kwish (go) + ish (CAUS) -> no analysis)", text);
        Assert.Contains("and 158 more. Next: motif proposal trial --project <fwdata> p", text);
        Assert.Contains("First word w0: touched by operation 4 (lexical/lexEntry/createAlternateForms) sharing form-1 (an overlap of ids, not a cause)", text);
        Assert.Single(text.TrimEnd().Split('\n'));
        Assert.Contains("Trace: motif parse w --trace --at baseline | motif parse w --trace --at p", text);
        Assert.DoesNotContain("caused by", text);
    }

    [Fact]
    public void AnOverlapThatWasNotComputedIsNotReportedAsNoOverlap()
    {
        var word = new RegressedWord("w", "case", 1, 0, ["a"], []);
        var text = TrialReviewTextRenderer.RenderRegressions(new TrialRegressions([new RegressionGroup(
            "new-word-failures", 1, 0, [word], "next")]));
        Assert.Contains("was not computed", text);
        Assert.DoesNotContain("no Proposal operation names", text);
    }

    [Fact]
    public void ALongLossListStopsAtTenAndCountsTheRest()
    {
        var words = Enumerable.Range(0, 25).Select(i => "w" + i).ToArray();
        var text = TrialReviewTextRenderer.RenderLossList(words);
        Assert.Equal("w0, w1, w2, w3, w4, w5, w6, w7, w8, w9 and 15 more; use --json for all", text);
        Assert.Equal("a, b", TrialReviewTextRenderer.RenderLossList(["a", "b"]));
    }
}
