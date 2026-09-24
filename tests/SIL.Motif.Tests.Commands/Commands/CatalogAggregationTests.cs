using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class CatalogAggregationTests
{
    [Fact]
    public void OverviewTimingUsesMedianNearestRankAndSlowestWords()
    {
        var result = TimingAggregation.SummarizeWords(
        [
            new AssessedWord("a", "analysed", [], 10),
            new AssessedWord("b", "analysed", [], 20),
            new AssessedWord("c", "analysed", [], 30),
            new AssessedWord("d", "capped", [], 40) { IsIncomplete = true },
            new AssessedWord("e", "skipped", [], null),
        ], top: 2);

        Assert.Equal(25, result.MedianMs);
        Assert.Equal(40, result.Percentile95Ms);
        Assert.Equal(1, result.StepLimitedWordCount);
        Assert.Equal(["d", "c"], result.SlowestWords.Select(word => word.Word));
    }

    [Fact]
    public void ObjectTimingAggregationGroupsAndRanksTheSelectedRuleWords()
    {
        AssessmentObjectTiming[] rows =
        [
            new("affix", "Verb template", "a", 20, 3, 4),
            new("affix", "Verb template", "b", 10, 2, 6),
            new("phonology", "Nasal harmony", "a", 4, 1, 2),
        ];

        var byKind = TimingAggregation.Aggregate(rows, "kind", rule: null, top: 10);
        Assert.Equal(["affix", "phonology"], byKind.Aggregates.Select(row => row.Name));
        Assert.Equal(10, byKind.Aggregates[0].ElapsedMs);
        Assert.Equal(10d / 12d, byKind.Aggregates[0].ShareOfTotal, precision: 6);
        Assert.Equal(30, byKind.Aggregates[0].Attempts);
        Assert.Equal(2, byKind.Aggregates[0].WordsTouched);

        var byRule = TimingAggregation.Aggregate(rows, "rule", "Verb template", top: 1);
        Assert.Equal(["Verb template", "Nasal harmony"], byRule.Aggregates.Select(row => row.Name));
        var costliest = Assert.Single(byRule.CostliestWords);
        Assert.Equal("b", costliest.Word);
        Assert.Equal(6, costliest.ElapsedMs);
        Assert.Equal(10, costliest.Attempts);
    }

    [Fact]
    public void TimedOutApprovedWordIsUnknownRatherThanAViolation()
    {
        var word = new AssessedWord("approved-form", "analysed", [], 900)
        {
            ProjectStanding = ProjectStanding.Approved,
            IsIncomplete = true,
            ReadingGrades = ["no-opinion"],
            MissedApprovedCount = 1,
        };

        var result = OverviewMetrics.Build([word.Word], null, [word]);

        Assert.Equal(1, result.TextCoverage.UnknownWords);
        Assert.Equal(1, result.Accuracy.UnknownWords);
        Assert.Equal(0, result.Accuracy.Violations);
    }

    [Fact]
    public void ComparePlacementRequiresEveryApprovedAnalysisAndUsesTimeoutAsUnknown()
    {
        var complete = CompareSemantics.Place(new CompareWordFacts(
            ProjectStanding.Approved, "analysed", false, ["approved"], MissedApprovedCount: 0));
        var partial = CompareSemantics.Place(new CompareWordFacts(
            ProjectStanding.Approved, "analysed", false, ["approved"], MissedApprovedCount: 1));
        var timeout = CompareSemantics.Place(new CompareWordFacts(
            ProjectStanding.Approved, "analysed", true, ["approved"], MissedApprovedCount: 1));

        Assert.Equal(CompareColumnKind.Match, complete.Column);
        Assert.Equal(CompareColumnKind.NoMatch, partial.Column);
        Assert.Equal(CompareColumnKind.Timeout, timeout.Column);
        Assert.Equal(CompareFamilyKind.Unknown,
            CompareSemantics.MeaningOf(timeout.Standing, timeout.Column).Family);
    }
}
