using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Store;
using SIL.Motif.Worker.Jobs;
using System.Text.Json;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

public sealed class TimingCostComparisonTests
{
    private const string Rule = "12345678-1234-1234-abcd-123456789abc";

    [Fact]
    public void SameWordsAndObjectCountersAreRequired()
    {
        var before = Record("before", 100, [Object("a", 30)]);
        var after = Record("after", 150, [Object("a", 60)]);
        var result = TimingCostComparisonQuery.Compare(before, after, ["a"]);
        Assert.Equal(50, result.Total.PercentChange);
        Assert.Equal(100, Assert.Single(result.Objects, row => row.Kind != "unattributed").Change.PercentChange);
        Assert.Null(TimingCostComparisonQuery.Compare(before, after, ["missing"]).Total.PercentChange);
    }

    [Fact]
    public void NewObjectAndMissingDetailHaveNoInventedPercent()
    {
        var before = Record("before", 100, []);
        var after = Record("after", 150, [Object("a", 60)]);
        Assert.Null(Assert.Single(TimingCostComparisonQuery.Compare(before, after, ["a"]).Objects, row => row.Kind != "unattributed").Change.PercentChange);
        var incomplete = before with { Details = ((AssessmentRecordDetails.Words)before.Details) with
            { ObjectTimings = [Object("a", 30)], TimingEvidence = new AssessmentTimingEvidence([], 0, false, 1) } };
        Assert.Null(Assert.Single(TimingCostComparisonQuery.Compare(incomplete, after, ["a"]).Objects, row => row.Kind != "unattributed").Change.PercentChange);
    }

    [Fact]
    public void NewAndMissingRulesRemainVisibleWithoutInventingZero()
    {
        var before = Record("before", 100, [Object("a", 30)]);
        var after = Record("after", 150, [Object("a", 60) with { Key = "22345678-1234-1234-abcd-123456789abc" }]);
        var changes = TimingCostComparisonQuery.Compare(before, after, ["a"]).Objects;
        Assert.Contains(changes, row => row.Key == Rule && row.Status == "missing" && row.Change.PercentChange is null);
        Assert.Contains(changes, row => row.Key != Rule && row.Status == "new" && row.Change.PercentChange is null);
    }

    [Fact]
    public void LocalOrdinalsInDifferentGrammarScopesAreNotCalledNewOrMissing()
    {
        var before = Record("before", 100, [Object("a", 20) with { Key = "1", IdentityQuality = "grammar-local", Scope = "old" }]);
        var after = Record("after", 150, [Object("a", 30) with { Key = "1", IdentityQuality = "grammar-local", Scope = "new" }]);
        var rows = TimingCostComparisonQuery.Compare(before, after, ["a"]).Objects.Where(row => row.Kind != "unattributed");
        Assert.All(rows, row => Assert.Equal("unavailable", row.Status));
        Assert.Equal(2, rows.Count());
    }

    [Fact]
    public void UnattributedWorkIsPairedOnlyWhenTheCompleteRemainderIsKnown()
    {
        var before = Record("before", 100, [Object("a", 20)]);
        var after = Record("after", 150, [Object("a", 30)]);
        var other = TimingCostComparisonQuery.Compare(before, after, ["a"]).Objects.Single(row => row.Kind == "unattributed");
        Assert.Equal(50, other.Change.PercentChange);
        var inconsistent = Record("before", 10, [Object("a", 20)]);
        Assert.Null(TimingCostComparisonQuery.Compare(inconsistent, after, ["a"]).Objects
            .Single(row => row.Kind == "unattributed").Change.PercentChange);
    }

    [Fact]
    public void PercentOfTotalShareIsNotPercentChange()
    {
        var before = Record("before", 100, [Object("a", 20)]);
        var after = Record("after", 200, [Object("a", 30)]);
        var result = TimingCostComparisonQuery.Compare(before, after, ["a"]);
        Assert.Equal(100, result.Total.PercentChange);
        Assert.Equal(50, Assert.Single(result.Objects, row => row.Kind != "unattributed").Change.PercentChange);
    }

    [Fact]
    public void MismatchedParserDoesNotCompare()
    {
        var before = Record("before", 100, [Object("a", 20)]);
        var after = Record("after", 200, [Object("a", 30)]);
        after = after with { Assessor = "other" };
        Assert.Null(TimingCostComparisonQuery.Compare(before, after, ["a"]).Total.PercentChange);
    }

    [Fact]
    public void UnknownComparatorDoesNotUseTodaysBaseline()
    {
        var after = Record("after", 200, [Object("a", 30)]);
        var comparison = TimingCostComparisonQuery.Read(null!, null, after, ["a"], false);
        Assert.Null(comparison.Total.PercentChange);
        Assert.Contains("No single paired Trial", comparison.Total.UnavailableReason);
    }

    [Fact]
    public void TrialPartitionMustMatchBothWritingSystemAndCaseIdentities()
    {
        var input = new TrialJobInput("{}", null, BaselineAssessmentIds: ["unrelated"],
            ReviewInputs: new ReviewInputReference("digest"), ReviewBaselines:
            [new ReviewBaselinePartition("ws-a", ["case-a"], ["before-a"]),
             new ReviewBaselinePartition("ws-b", ["case-b"], ["before-b"])]);
        var result = JsonSerializer.Serialize(new { reviewAfter = new[]
            { new ReviewAfterPartition("ws-b", ["case-b"], ["after-b"]) } });
        Assert.Equal(["before-b"], TimingCostComparisonQuery.BaselineIds(input, result, "after-b"));
        Assert.Empty(TimingCostComparisonQuery.BaselineIds(input, result, "after-a"));
        Assert.Empty(TimingCostComparisonQuery.BaselineIds(input, "[]", "after-b"));
        Assert.Empty(TimingCostComparisonQuery.BaselineIds(input, "{\"reviewAfter\":[{}]}", "after-b"));
        var changedCases = JsonSerializer.Serialize(new { reviewAfter = new[]
            { new ReviewAfterPartition("ws-b", ["case-a"], ["after-b"]) } });
        Assert.Empty(TimingCostComparisonQuery.BaselineIds(input, changedCases, "after-b"));
        var unknownWritingSystem = JsonSerializer.Serialize(new { reviewAfter = new[]
            { new ReviewAfterPartition(null, ["case-b"], ["after-b"]) } });
        var unknownInput = input with { ReviewBaselines = [new ReviewBaselinePartition(null, ["case-b"], ["before-unknown"])] };
        Assert.Empty(TimingCostComparisonQuery.BaselineIds(unknownInput, unknownWritingSystem, "after-b"));
    }

    [Fact]
    public void AuthoredObjectIdentityIgnoresGuidCase()
    {
        var before = Record("before", 100, [Object("a", 30) with { Key = Rule.ToUpperInvariant() }]);
        var after = Record("after", 150, [Object("a", 60)]);
        Assert.Equal(100, Assert.Single(TimingCostComparisonQuery.Compare(before, after, ["a"]).Objects, row => row.Kind != "unattributed").Change.PercentChange);
    }

    private static AssessmentObjectTiming Object(string word, long work) =>
        new("morph_rule", Rule, "authored", "forward", "rule", word, null, null, 999) { ParserWork = work };

    private static AssessmentRecord Record(string id, long work, IReadOnlyList<AssessmentObjectTiming> objects)
    {
        var words = new[] { new AssessedWord("a", "analysed", []) { ParserWork = work, ElapsedMs = 500 } };
        var invocation = new BatchInvocationEvidence(id, "source", "source-hash", "parser-hash", "words", "words-hash",
            "tsv", "tsv-hash", "stderr", "stderr-hash", null, new ParserWorkLimit(200), 1, true);
        return new AssessmentRecord(id, null, null, "pangloss", "ParseTime", "{}", "scope",
            new AssessmentSubject.Words(Selection.Create("test", ["a"]), "none", "1"), "baseline", null, null,
            "grammar", null, null, null, "2026-09-24T12:00:00Z",
            new AssessmentRecordDetails.Words(words, null, null, objects, invocation)
            { TimingEvidence = new AssessmentTimingEvidence([], 1, true, objects.Count) });
    }
}
