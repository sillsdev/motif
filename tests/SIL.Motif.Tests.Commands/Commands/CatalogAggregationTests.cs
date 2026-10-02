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
    public void OverviewTimingSplitsTotalWordTimeByKindWithTheResidualBeside()
    {
        AssessedWord[] words = [Timed("a", 500), Timed("b", 300)];
        AssessmentObjectTiming[] rows =
            [Row("morph_rule", "rule-r", "R", "a", 300), Row("phon_rule", "rule-p", "P", "b", 100)];

        var result = TimingAggregation.SummarizeWords(words, rows);

        Assert.Equal(["morph_rule", "phon_rule"], result.Kinds.Select(kind => kind.Key));
        Assert.Equal(0.375, result.Kinds[0].ShareOfWordTime!.Value, precision: 6);
        Assert.Equal(800, result.Attribution.WordTimeMs);
        Assert.Equal(400, result.Attribution.NotAttributedMs);
    }

    [Fact]
    public void ObjectTimingAggregationGroupsByKindAndRanksTheSelectedRuleWords()
    {
        AssessedWord[] words = [Timed("a", 30), Timed("b", 20)];
        AssessmentObjectTiming[] rows =
        [
            new("affix", "affix-a", "authored", "analysis", "Verb template", "a", 20, null, 4_000_000),
            new("affix", "affix-a", "authored", "analysis", "Verb template", "b", 10, null, 6_000_000),
            new("affix", "affix-a", "authored", "synthesis", "Verb template", "b", 6, null, null),
            new("phonology", "phon-rule", "authored", "analysis", "Nasal harmony", "a", 4, null, 2_000_000),
        ];

        var byKind = TimingAggregation.Aggregate(words, rows, "kind", rule: null, top: 10);
        Assert.Equal(["affix", "phonology"], byKind.Aggregates.Select(row => row.Key));
        Assert.Equal(["affix", "phonology"], byKind.Aggregates.Select(row => row.Kind));
        Assert.Equal(10, byKind.Aggregates[0].SelfMs);
        Assert.Equal(10d / 50d, byKind.Aggregates[0].ShareOfWordTime!.Value, precision: 6);
        Assert.Equal(36, byKind.Aggregates[0].Calls);
        Assert.Equal(2, byKind.Aggregates[0].WordsTouched);

        var byRule = TimingAggregation.Aggregate(words, rows, "rule", "affix-a", top: 1);
        Assert.Equal(["Verb template", "Nasal harmony"], byRule.Aggregates.Select(row => row.Name));
        Assert.Equal(["affix-a", "phon-rule"], byRule.Aggregates.Select(row => row.Key));
        Assert.Equal(["authored", "authored"], byRule.Aggregates.Select(row => row.IdentityQuality));
        var costliest = Assert.Single(byRule.CostliestWords);
        Assert.Equal("b", costliest.Word);
        Assert.Equal(6, costliest.SelfMs);
        Assert.Equal(16, costliest.Calls);
        Assert.Equal(20, costliest.WordTimeMs);
        Assert.Equal(6d / 20d, costliest.ShareOfWordTime!.Value, precision: 6);
    }

    [Fact]
    public void ARulesShareIsOfTotalWordTimeNotOfTheTimeRulesRecorded()
    {
        AssessedWord[] words = [Timed("a", 500), Timed("b", 300)];
        AssessmentObjectTiming[] rows =
        [
            Row("morph_rule", "rule-r", "R", "a", 300),
            Row("phon_rule", "rule-p", "P", "b", 300),
        ];

        var result = TimingAggregation.Aggregate(words, rows, "rule", rule: null, top: 10);

        Assert.Equal(0.375, result.Aggregates.Single(row => row.Key == "rule-r").ShareOfWordTime!.Value, precision: 6);
        Assert.Equal(800, result.Attribution.WordTimeMs);
        Assert.Equal(600, result.Attribution.AttributedMs);
        Assert.Equal(200, result.Attribution.NotAttributedMs);
        Assert.Equal(0.25, result.Attribution.NotAttributedShare!.Value, precision: 6);
        Assert.False(result.Attribution.Overrun);
    }

    [Fact]
    public void ASelectionShareAddsTheTimesBeforeDividingRatherThanAveragingEachWordsShare()
    {
        AssessedWord[] words = [Timed("a", 700), Timed("b", 48)];
        AssessmentObjectTiming[] rows =
            [Row("morph_rule", "rule-r", "R", "a", 180), Row("morph_rule", "rule-r", "R", "b", 20)];

        var share = Assert.Single(TimingAggregation.Aggregate(words, rows, "rule", rule: null, top: 10).Aggregates)
            .ShareOfWordTime!.Value;

        Assert.Equal(200d / 748d, share, precision: 6);
        Assert.Equal(0.267, share, precision: 3);
    }

    [Fact]
    public void AStoppedWordsTimeIsWhatItSpentBeforeTheLimit()
    {
        AssessedWord[] words =
        [
            Timed("stopped", 900) with { Outcome = "capped", IsIncomplete = true },
            Timed("finished", 100),
        ];
        AssessmentObjectTiming[] rows =
            [Row("morph_rule", "rule-r", "R", "stopped", 300), Row("morph_rule", "rule-r", "R", "finished", 50)];

        var result = TimingAggregation.Aggregate(words, rows, "kind", rule: null, top: 10);

        Assert.Equal(1000, result.Attribution.WordTimeMs);
        Assert.Equal(0.35, Assert.Single(result.Aggregates).ShareOfWordTime!.Value, precision: 6);
    }

    [Fact]
    public void AnOverrunIsAFlagAndNeverANegativeOrHiddenResidual()
    {
        AssessedWord[] words = [Timed("over", 10), Timed("under", 20)];
        AssessmentObjectTiming[] rows =
            [Row("morph_rule", "rule-r", "R", "over", 12), Row("morph_rule", "rule-r", "R", "under", 5)];

        var attribution = TimingAggregation.Aggregate(words, rows, "kind", rule: null, top: 10).Attribution;

        Assert.True(attribution.Overrun);
        Assert.Equal(2, attribution.OverrunMs, precision: 6);
        Assert.Equal(15, attribution.NotAttributedMs!.Value, precision: 6);
    }

    [Fact]
    public void WordTimeKeepsNanosecondsSoASubMillisecondWordStillHasADenominator()
    {
        AssessedWord[] words = [new("quick", "analysed", [], 0) { ElapsedNs = 400_000 }];
        AssessmentObjectTiming[] rows = [new("morph_rule", "rule-r", "authored", "analysis", "R", "quick", 1, null, 300_000)];

        var result = TimingAggregation.Aggregate(words, rows, "rule", rule: null, top: 10);

        Assert.Equal(0.4, result.Attribution.WordTimeMs, precision: 9);
        Assert.Equal(0.75, Assert.Single(result.Aggregates).ShareOfWordTime!.Value, precision: 6);
        Assert.Equal(0.1, result.Attribution.NotAttributedMs!.Value, precision: 9);
    }

    [Fact]
    public void TwoObjectsSharingALabelStayTwoRowsAndUncountedCallsStayUnknown()
    {
        AssessedWord[] words = [Timed("a", 100)];
        AssessmentObjectTiming[] rows =
        [
            new("morph_rule", "guid-1", "authored", "analysis", "Plural", "a", 3, null, 10_000_000),
            new("morph_rule", "guid-2", "authored", "analysis", "Plural", "a", 5, null, 20_000_000),
            new("lex_entry", "entry-1", "authored", "analysis", "dog", "a", null, null, 5_000_000),
        ];

        var byRule = TimingAggregation.Aggregate(words, rows, "rule", rule: null, top: 10);
        var byKind = TimingAggregation.Aggregate(words, rows, "kind", rule: null, top: 10);

        Assert.Equal(["guid-2", "guid-1", "entry-1"], byRule.Aggregates.Select(row => row.Key));
        Assert.Equal(["Plural", "Plural", "dog"], byRule.Aggregates.Select(row => row.Name));
        Assert.Equal([5L, 3L, null], byRule.Aggregates.Select(row => row.Calls));
        Assert.Equal([8L, null], byKind.Aggregates.Select(row => row.Calls));
    }

    [Fact]
    public void ARuleIsNamedByItsKeyOrByALabelOnlyOneObjectCarries()
    {
        AssessmentObjectTiming[] rows =
        [
            new("morph_rule", "guid-1", "authored", "analysis", "Plural", "a", 3, null, 10_000_000),
            new("morph_rule", "guid-2", "authored", "analysis", "Plural", "a", 5, null, 20_000_000),
            new("phon_rule", "guid-3", "authored", "analysis", "Nasal harmony", "a", 1, null, 1_000_000),
        ];

        Assert.Equal(["guid-1"], TimingAggregation.ResolveRule(rows, "guid-1"));
        Assert.Equal(["guid-3"], TimingAggregation.ResolveRule(rows, "Nasal harmony"));
        Assert.Equal(["guid-1", "guid-2"], TimingAggregation.ResolveRule(rows, "Plural"));
        Assert.Empty(TimingAggregation.ResolveRule(rows, "Missing"));
    }

    [Fact]
    public void ObjectTimeInAWordWithoutAParseTimeIsLeftOutOfEveryShare()
    {
        AssessedWord[] words = [Timed("timed", 100), new("untimed", "skipped", [], null)];
        AssessmentObjectTiming[] rows =
            [Row("morph_rule", "rule-r", "R", "timed", 40), Row("morph_rule", "rule-r", "R", "untimed", 60)];

        var result = TimingAggregation.Aggregate(words, rows, "rule", rule: null, top: 10);

        Assert.Equal(40, Assert.Single(result.Aggregates).SelfMs);
        Assert.Equal(1, result.Attribution.MeasuredWordCount);
        Assert.Equal(60, result.Attribution.NotAttributedMs);
    }

    [Theory]
    [InlineData("12345678-1234-1234-ABCD-123456789ABC")]
    [InlineData("{12345678-1234-1234-abcd-123456789abc}")]
    public void ARuleGuidResolvesAndFiltersByValue(string requested)
    {
        const string key = "12345678-1234-1234-abcd-123456789abc";
        AssessmentObjectTiming[] rows = [Row("morph_rule", key, "Plural", "a", 10)];

        Assert.Equal([key], TimingAggregation.ResolveRule(rows, requested));
        var detail = TimingAggregation.Aggregate([Timed("a", 20)], rows, "rule", requested, top: 10);
        Assert.Equal("a", Assert.Single(detail.CostliestWords).Word);
        Assert.Equal(10, detail.CostliestWords[0].SelfMs);
    }

    [Fact]
    public void AStructuralTimingKeyKeepsOrdinalIdentity()
    {
        AssessmentObjectTiming[] rows = [Row("morph_rule", "local/Rule", "Plural", "a", 10)];

        Assert.Empty(TimingAggregation.ResolveRule(rows, "local/rule"));
        Assert.Empty(TimingAggregation.Aggregate([Timed("a", 20)], rows, "rule", "local/rule", 10).CostliestWords);
    }

    [Fact]
    public void WithNoObjectTimeRecordedNoTimeIsCalledNotAttributed()
    {
        var attribution = TimingAggregation.Aggregate([Timed("a", 10)], [], "kind", rule: null, top: 10).Attribution;

        Assert.Equal(10, attribution.WordTimeMs);
        Assert.Null(attribution.NotAttributedMs);
        Assert.Null(attribution.NotAttributedShare);
    }

    [Theory]
    [InlineData(300_000, 100_000, 200_000, 0)]
    [InlineData(300_000, 100_000, 200_001, 1)]
    public void NanosecondPartitionsPreserveExactResiduals(long whole, long first, long second, long overrun)
    {
        AssessedWord[] words = [new("quick", "analysed", [], 0) { ElapsedNs = whole }];
        AssessmentObjectTiming[] rows =
        [
            new("morph_rule", "r", "authored", "analysis", "R", "quick", 1, null, first),
            new("morph_rule", "r", "authored", "synthesis", "R", "quick", 1, null, second),
        ];

        var result = TimingAggregation.Aggregate(words, rows, "rule", "r", 10);

        Assert.Equal(overrun > 0, result.Attribution.Overrun);
        Assert.Equal(overrun / 1_000_000d, result.Attribution.OverrunMs);
        Assert.Equal(0, result.Attribution.NotAttributedMs);
        Assert.Equal((first + second) / 1_000_000d, result.Attribution.AttributedMs);
        Assert.Equal((first + second) / 1_000_000d, Assert.Single(result.Aggregates).SelfMs);
        Assert.Equal((first + second) / 1_000_000d, Assert.Single(result.CostliestWords).SelfMs);
    }

    [Fact]
    public void MillisecondFallbackAndNanosecondRowsShareAnExactUnit()
    {
        AssessedWord[] words = [new("fallback", "analysed", [], 1)];
        AssessmentObjectTiming[] rows =
        [
            new("morph_rule", "r", "authored", "analysis", "R", "fallback", 1, null, 300_000),
            new("morph_rule", "r", "authored", "synthesis", "R", "fallback", 1, null, 700_000),
        ];

        var attribution = TimingAggregation.Aggregate(words, rows, "kind", null, 10).Attribution;

        Assert.Equal(1, attribution.WordTimeMs);
        Assert.Equal(0, attribution.NotAttributedMs);
        Assert.False(attribution.Overrun);
    }

    private static AssessedWord Timed(string word, int ms) =>
        new(word, "analysed", [], ms) { ElapsedNs = ms * 1_000_000L };

    private static AssessmentObjectTiming Row(string kind, string key, string label, string word, int ms) =>
        new(kind, key, "authored", "analysis", label, word, 1, null, ms * 1_000_000L);

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

    private static ParseWordEvidence Parsed(int readings) => new("v1", 0, "word", 1, false, false, false,
        Enumerable.Range(0, readings).Select(index => new ParseAnalysis(
            [new ParseMorph($"form-{index}", "v", null, null)])).ToArray(), []);

    [Fact]
    public void ComparePlacementRequiresEveryApprovedAnalysisAndUsesTimeoutAsUnknown()
    {
        var complete = CompareSemantics.Place(new CompareWordFacts(
            ProjectStanding.Approved, "analysed", false, Parsed(1), ["approved"], MissedApprovedCount: 0));
        var partial = CompareSemantics.Place(new CompareWordFacts(
            ProjectStanding.Approved, "analysed", false, Parsed(1), ["approved"], MissedApprovedCount: 1));
        var timeout = CompareSemantics.Place(new CompareWordFacts(
            ProjectStanding.Approved, "analysed", true, null, ["approved"], MissedApprovedCount: 1));
        var extra = CompareSemantics.Place(new CompareWordFacts(
            ProjectStanding.Approved, "analysed", false, Parsed(2), ["approved", "no-opinion"], MissedApprovedCount: 0));

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
            (new CompareWordFacts(ProjectStanding.Approved, "analysed", false, Parsed(1), ["no-opinion"], 1), oneMissed,
                FixFirstCategory.ApprovedNoMatch, 2, "Approved × No match", "Expected o- up, not built."),
            (new CompareWordFacts(ProjectStanding.Rejected, "analysed", false, Parsed(1), ["disapproved"], 0), Array.Empty<ParserReading>(),
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
