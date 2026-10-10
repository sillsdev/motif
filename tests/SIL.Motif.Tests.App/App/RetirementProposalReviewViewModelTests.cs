using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class RetirementProposalReviewViewModelTests
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void ReviewShowsTheWholeProposalCountsReadingEvidenceAndResolvedStateSeparately()
    {
        var view = new RetirementProposalReviewViewModel(Projection());

        Assert.Equal("1 word affected", view.AffectedWordsText);
        Assert.Equal("1 form and writing system combination checked", view.AffectedFormCasesText);
        Assert.Equal(4, view.Parts.Count);
        Assert.Equal("Sound changes", view.Parts[0].Title);
        Assert.Contains("Sound rule enabled", view.Parts[0].Summaries);
        Assert.Contains("Other uses moved to the remaining form", view.Parts[2].Summaries);
        Assert.Contains("2 other references affected", view.StatisticsRows);
        Assert.Contains("1 Approved readings: 1 preserved", view.ReadingResultsText, StringComparison.Ordinal);
        var reading = Assert.Single(view.AffectedReadings);
        Assert.Equal("ata", reading.Word);
        Assert.Equal("A", reading.OpinionGlyph);
        Assert.Equal("Approved", reading.Opinion);
        Assert.Equal("Written form: ata", reading.SurfaceText);
        Assert.Equal("Preserved", reading.Verification);
        Assert.Equal("The sound rule was traced for this reading", reading.RuleAttribution);
        Assert.Equal("Resolved", view.FindingState);
        Assert.Equal("Problem examples: 2 of 2 before; 0 of 1 after", view.FindingCountsText);
        Assert.Contains("1 Approved analysis", view.UnresolvedText, StringComparison.Ordinal);
        Assert.Contains("1 restriction", view.UnresolvedText, StringComparison.Ordinal);
        Assert.Contains("2 other uses", view.UnresolvedText, StringComparison.Ordinal);
    }

    [Fact]
    public void PendingReviewNamesEvidenceThatIsNotAvailableYet()
    {
        var view = new RetirementProposalReviewViewModel(new RetirementReviewQueryResponse(true,
            "proposal/one", "waiting-for-dry-run", null, null, null,
            ["A Dry Run is not available yet.", "Frozen affected-reading expectations are not available yet."]));

        Assert.False(view.HasCompleteReview);
        Assert.True(view.HasPendingEvidence);
        Assert.Contains("A Dry Run is not available yet.", view.PendingEvidenceText, StringComparison.Ordinal);
        Assert.Empty(view.AffectedReadings);
        Assert.Empty(view.Parts);
    }

    [Fact]
    public void PendingReviewStillShowsTheDraftsDryRunChanges()
    {
        var dryRun = new DryRunProjection("proposal/one", "intent/one", "Baseline", [],
            "effects/one", "footprint/one")
        {
            Operations =
            [
                new("operation/rule", "grammar/phSegmentRule/setDisabled", null, null, [], "{\"value\":false}"),
                new("operation/bundle", "analysis/wfiMorphBundle/setMorph", null, null, [], null),
                new("operation/delete", "lexical/lexEntry/deleteAlternateForm", null, null, [], null),
            ],
        };
        var view = new RetirementProposalReviewViewModel(new RetirementReviewQueryResponse(true,
            "proposal/one", "waiting-for-evidence", "job/one", dryRun, null,
            ["Before-and-after parser results are not available yet."]));

        Assert.True(view.HasPendingEvidence);
        Assert.True(view.HasDryRun);
        Assert.Contains("3 operations", view.DryRunSummaryText, StringComparison.Ordinal);
        Assert.Contains("Sound class and rule changes: 1", view.DryRunSummaryText, StringComparison.Ordinal);
        Assert.Contains("Affected analysis changes: 1", view.DryRunSummaryText, StringComparison.Ordinal);
        Assert.Contains("Alternate forms marked for deletion: 1", view.DryRunSummaryText, StringComparison.Ordinal);
    }

    private static RetirementProposalReviewProjection Projection() => new("proposal/one", "intent/one",
        new DryRunProjection("proposal/one", "intent/one", "Baseline", [], "effects/one", "footprint/one")
        {
            Operations =
            [
                new("operation/rule", "grammar/phSegmentRule/setDisabled", null, null, [], "{\"value\":false}"),
                new("operation/bundle", "analysis/wfiMorphBundle/setMorph", null, null,
                    ["operation/rule"], null),
                new("operation/reference", "grammar/moAlloAdhocProhib/retargetReferences", null, null,
                    ["operation/rule"], null),
                new("operation/delete", "lexical/lexEntry/deleteAlternateForm", null, null,
                    ["operation/bundle", "operation/reference"], null),
            ],
        },
        new(new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0), 1, [], 2, 1, 1, 0,
            new("finding/one", 2, 2, 0, 1, Digest, Digest, true), Digest),
        [new("rule", "New rule and sound class", ["operation/rule"], []),
         new("bundle", "Analyses and bundle text", ["operation/bundle"], []),
         new("other-references", "Other references", ["operation/reference"], []),
         new("retired-form", "Deleted forms", ["operation/delete"], [])],
        [new("case/one", "reading/one", "wordform/one", "ata", "qaa", "ata", "ata", "approved", "A",
            "preserved", true, true, true, "traced", "rule/one", Digest, null)],
        new("finding/one", 2, 2, 0, 1, Digest, Digest, true, false),
        new(1, 1, 2, 0, []), []);
}
