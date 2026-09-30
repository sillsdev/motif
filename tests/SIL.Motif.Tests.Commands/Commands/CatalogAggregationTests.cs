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
    public void TimingWordSetParsesNamedChoicesAndMatrixCellsAsDistinctCases()
    {
        Assert.IsType<TimingWordSet.All>(TimingWordSet.Parse("all"));
        Assert.IsType<TimingWordSet.StepLimited>(TimingWordSet.Parse("step-limit"));
        Assert.IsType<TimingWordSet.Slowest>(TimingWordSet.Parse("slowest"));
        var cell = Assert.IsType<TimingWordSet.MatrixCell>(TimingWordSet.Parse("cell:approved:no-parse"));
        Assert.Equal(TimingStanding.Approved, cell.Standing);
        Assert.Equal(CompareColumnKind.NoParse, cell.Column);
        Assert.Equal("cell:approved:no-parse", cell.ToWireValue());
        Assert.Equal("Saved set", Assert.IsType<TimingWordSet.Named>(TimingWordSet.Parse("Saved set")).Name);
        Assert.Null(TimingWordSet.Parse("cell:approved:misspelled"));
    }

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
        Assert.Equal("affix", byKind.Aggregates[0].Kind);

        var byRule = TimingAggregation.Aggregate(rows, "rule", "Verb template", top: 1);
        Assert.Equal(["Verb template", "Nasal harmony"], byRule.Aggregates.Select(row => row.Name));
        Assert.Equal(["affix", "phonology"], byRule.Aggregates.Select(row => row.Kind));
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
            ProjectStanding.Approved, "analysed", false, null, ["approved"], MissedApprovedCount: 0));
        var partial = CompareSemantics.Place(new CompareWordFacts(
            ProjectStanding.Approved, "analysed", false, null, ["approved"], MissedApprovedCount: 1));
        var timeout = CompareSemantics.Place(new CompareWordFacts(
            ProjectStanding.Approved, "analysed", true, null, ["approved"], MissedApprovedCount: 1));
        var extra = CompareSemantics.Place(new CompareWordFacts(
            ProjectStanding.Approved, "analysed", false, null, ["approved", "no-opinion"], MissedApprovedCount: 0));

        Assert.Equal(CompareColumnKind.Match, complete.Column);
        Assert.Equal(CompareColumnKind.NoMatch, partial.Column);
        Assert.Equal(CompareColumnKind.Timeout, timeout.Column);
        Assert.Equal(CompareColumnKind.NoMatch, extra.Column);
        Assert.Equal(CompareFamilyKind.Unknown,
            CompareSemantics.MeaningOf(timeout.Standing, timeout.Column).Family);
    }

    [Fact]
    public void FixFirstPriorityComesFromOneCommandRuleTable()
    {
        static ParserReading Reading(string form, string gloss) =>
            new([new ParserReadingMorph(form, gloss, "v", null, false, null)]);

        var oneMissed = new[] { Reading("o-", "up") };
        var twoMissed = new[] { Reading("o-", "up"), Reading("ka", "walk") };
        var cases = new[]
        {
            (new CompareWordFacts(ProjectStanding.Approved, "no-analysis", false, null, [], 2), twoMissed,
                FixFirstCategory.ApprovedNoParse, 1, "Approved × No parse", "Expected o- up, not built (2 approved analyses missed)."),
            (new CompareWordFacts(ProjectStanding.Approved, "analysed", false, null, ["no-opinion"], 1), oneMissed,
                FixFirstCategory.ApprovedNoMatch, 2, "Approved × No match", "Expected o- up, not built."),
            (new CompareWordFacts(ProjectStanding.Rejected, "analysed", false, null, ["disapproved"], 0), Array.Empty<ParserReading>(),
                FixFirstCategory.RejectedRebuilt, 3, "Rejected but rebuilt", "The parser rebuilt an analysis the project rejected."),
            (new CompareWordFacts(ProjectStanding.Candidate, "no-analysis", false, null, [], 0), Array.Empty<ParserReading>(),
                FixFirstCategory.CandidateNoParse, 4, "Candidate × No parse", "The parser could not rebuild this candidate."),
        };

        foreach (var (facts, missed, category, rank, label, explanation) in cases)
        {
            var priority = CompareSemantics.FixFirst(facts, missed);

            Assert.NotNull(priority);
            Assert.Equal(category, priority.Category);
            Assert.Equal(rank, priority.Rank);
            Assert.Equal(label, priority.Label);
            Assert.Equal(explanation, priority.Explanation);
            Assert.DoesNotContain(Environment.NewLine, priority.Explanation);
        }

        Assert.Null(CompareSemantics.FixFirst(
            new CompareWordFacts(ProjectStanding.Approved, "timed-out", true, null, [], 1), oneMissed));
    }
}
