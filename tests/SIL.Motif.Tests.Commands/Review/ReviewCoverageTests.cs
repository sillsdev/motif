using SIL.Motif.Contract.Review;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Runtime.Review;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

public sealed class ReviewCoverageTests
{
    [Fact]
    public void RepeatedSampleTokenWeightsAffectOnlyTokenCoverage()
    {
        var cases = new[] { Case("a", true, true, [Token("a1", 3), Token("a2", 2)]), Case("b", false, true, [Token("b", 1)]) };
        var coverage = ReviewCoverage.Tokens(cases, before: true);
        Assert.Equal(6, coverage.Denominator);
        Assert.Equal(5, coverage.Numerator);
    }

    [Fact]
    public void UnmeasuredTokensRemainInDenominator()
    {
        var coverage = ReviewCoverage.Tokens([Case("a", true, null, [Token("a", 2)])], before: false);
        Assert.Equal(2, coverage.Denominator);
        Assert.Equal(2, coverage.Unknown);
        Assert.Equal(CoveragePrecision.LowerBound, coverage.Precision);
        var inconsistent = Case("b", true, false, [Token("b", 1)]) with
        { After = Side(false) with { Completion = Completion.Stopped } };
        var stopped = ReviewCoverage.Tokens([inconsistent], false);
        Assert.Equal(1, stopped.Unknown);
        Assert.Equal(0, stopped.KnownNoMatch);
    }

    [Fact]
    public void CellVariantAlternativesCountOneCell()
    {
        var coverage = ReviewCoverage.Cells([CellCase("a", "cell", "e1", false), CellCase("b", "cell", "e2", true)], false);
        Assert.Equal(1, coverage.Denominator);
        Assert.Equal(1, coverage.Numerator);
    }

    [Fact]
    public void SameSpellingMustMatchEachCellsExpectedReading()
    {
        var coverage = ReviewCoverage.Cells([CellCase("a", "first", "e1", true), CellCase("a", "second", "e2", false)], false);
        Assert.Equal(2, coverage.Denominator);
        Assert.Equal(1, coverage.Numerator);
    }

    [Fact]
    public void EmptySetHasNullPercent()
    {
        var result = ReviewCoverage.Count([]);
        Assert.Null(result.Percent);
        Assert.Contains("empty-set", result.ReasonCodes);
    }

    [Fact]
    public void PartialEvidenceHasNoExactDeltaPoints()
    {
        var exact = ReviewCoverage.Count([(true, 1L)]);
        var partial = ReviewCoverage.Count([((bool?)null, 1L)]);
        Assert.Null(ReviewCoverage.DeltaPoints(exact, partial, true));
        Assert.Null(ReviewCoverage.DeltaPoints(exact, exact, false));
        var stopped = Case("a", true, true, [Token("a", 1)]) with
        { After = Side(true) with { Completion = Completion.Stopped } };
        var observedPartial = ReviewCoverage.Tokens([stopped], false);
        Assert.Equal(CoveragePrecision.LowerBound, observedPartial.Precision);
        Assert.Null(ReviewCoverage.DeltaPoints(exact, observedPartial, true));
    }

    [Fact]
    public void ReadingLossAcrossTwoAuthoritiesDeduplicatesIdentity()
    {
        var accumulator = new ReviewSummaryAccumulator();
        var loss = new ReviewReadingLoss("reading", true, true, ["a", "b"]);
        accumulator.Add(Case("a", true, false, []) with { ReadingLosses = [loss] });
        accumulator.Add(Case("a", true, false, []) with { ReadingLosses = [loss] });
        Assert.Equal(1, accumulator.Snapshot().UniqueReadingLosses);
    }

    [Fact]
    public void IncompleteUnionCountsEachCaseAndWordOnce()
    {
        var accumulator = new ReviewSummaryAccumulator();
        accumulator.Add(Case("a", null, null, []));
        var result = accumulator.Snapshot();
        Assert.Equal(1, result.IncompleteCases);
        Assert.Equal(1, result.IncompleteWords);
        Assert.Equal(1, result.IncompleteBefore);
        Assert.Equal(1, result.IncompleteAfter);
        Assert.Equal(1, result.BeforeIncomplete.NotMeasured);
        Assert.Equal(1, result.AfterIncomplete.NotMeasured);
    }

    [Fact]
    public void StopReasonsCountCasesOnceAndPreserveTokenWeight()
    {
        var stopped = Side(null) with { Completion = Completion.Stopped, StopReasons = ["work-limit", "work-limit", "time-limit"] };
        var item = Case("a", true, null, [Token("position", 4)]) with { After = stopped };
        var accumulator = new ReviewSummaryAccumulator();
        accumulator.Add(item);
        var result = accumulator.Snapshot();
        Assert.Equal(1, result.AfterIncomplete.Stopped);
        Assert.Equal(0, result.AfterIncomplete.NotMeasured);
        Assert.Equal(1, Assert.Single(result.AfterIncomplete.Reasons, reason => reason.Reason == "work-limit").Cases);
        Assert.Equal(1, Assert.Single(result.AfterIncomplete.Reasons, reason => reason.Reason == "time-limit").Cases);
        Assert.Equal(4, result.IncompleteTokenWeight);
    }

    [Fact]
    public void EmptyNamedSetsRemainInTheSnapshot()
    {
        var accumulator = new ReviewSummaryAccumulator([new ReviewSetBinding("id", "name", "sampled-tokens",
            "membership", "expectations", "source", [])]);
        var snapshot = accumulator.Snapshot();
        Assert.Equal(1, snapshot.SetCount);
        Assert.Null(snapshot.TokensAfter.Percent);
    }

    [Fact]
    public void SectionCountsKeepUnchangedCasesAndNamedRoleCounts()
    {
        var set = new ReviewSetBinding("tokens", "Tokens", "sampled-tokens", "m", "e", "s", []);
        var accumulator = new ReviewSummaryAccumulator([set]);
        accumulator.Add(Case("a", true, true, [Token("position", 3)]));
        var snapshot = accumulator.Snapshot();
        Assert.Equal(1, snapshot.SetCountsByRole["sampled-tokens"]);
        Assert.Equal(1, Assert.Single(snapshot.Sections, section => section.Section == "wordforms").Total);
        Assert.Equal(1, Assert.Single(snapshot.Sections, section => section.Section == "sampled-tokens").Total);
    }

    [Fact]
    public void ApprovedRetentionCountsEachExactReadingOnceAcrossAssertions()
    {
        var morphology = new ApprovedMorphology([new ApprovedMorph("11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222", null, [])]);
        var a = new ReviewAssertion("a", "reading", morphology, ReferenceAuthority.Human, "actor", "revision", true, false, true, []);
        var b = a with { Id = "b" };
        var item = Case("word", true, true, []) with
        {
            Assertions = [a, b],
            Before = Side(true, ["a", "b"]),
            After = Side(true, [], ["a", "b"]),
            ReadingLosses = [new ReviewReadingLoss("reading", true, false, ["a", "b"])],
        };
        var accumulator = new ReviewSummaryAccumulator();
        accumulator.Add(item);
        var summary = accumulator.Snapshot();
        Assert.Equal(1, summary.ApprovedExpected);
        Assert.Equal(1, summary.ApprovedBeforeMatched);
        Assert.Equal(0, summary.ApprovedAfterMatched);
        Assert.Equal(1, summary.ApprovedNewlyLost);
    }

    [Fact]
    public void UnresolvedApprovedIdentityRemainsAnUnknownExpectation()
    {
        var assertion = new ReviewAssertion("a", "", null, ReferenceAuthority.Human, "actor", "revision", true, false, true, []);
        var accumulator = new ReviewSummaryAccumulator();
        accumulator.Add(Case("word", true, true, []) with { Assertions = [assertion] });
        var summary = accumulator.Snapshot();
        Assert.Null(summary.ApprovedExpected);
        Assert.Null(summary.ApprovedAfterUnknown);
        Assert.Contains("expectation-identity-unavailable", summary.Limitations);

        var malformed = assertion with { ReadingIdentity = "identity", Morphology = new ApprovedMorphology([]) };
        var second = new ReviewSummaryAccumulator();
        second.Add(Case("word", true, true, []) with
        { Assertions = [malformed], Limitations = ["expectation-identity-unavailable"] });
        Assert.Null(second.Snapshot().ApprovedExpected);
    }

    [Fact]
    public void WorkAndFindingFlagsUseTheirOwnCompleteComparisons()
    {
        var scope = new ReviewWorkScope("scope", "baseline", "assessor", "hash", "counter", "settings", "tokenizer");
        var work = new ReviewWorkInputs(scope, scope, ["word"], [new("word", true, 100)], [new("word", true, 121)]);
        var warning = new ReviewDiagnostic(null, "check", "code", ["subject"], [], "warning");
        var findings = new ReviewFindingInputs([], [warning], [], []);
        var accumulator = new ReviewSummaryAccumulator();
        accumulator.SetWork(work);
        accumulator.SetFindings(findings);
        var snapshot = accumulator.Snapshot();
        Assert.Equal([ReviewFlagId.ParserWorkIncrease, ReviewFlagId.NewWarningsOrParsimony], snapshot.Flags);
        Assert.Equal(21, snapshot.ParserWork!.PercentChange);
        Assert.Single(snapshot.Findings!.AddedWarnings);
    }

    [Fact]
    public void ControlRollupsKeepSetWordAndReadingCountsSeparate()
    {
        var morphology = new ApprovedMorphology([new ApprovedMorph("11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222", null, [])]);
        var assertion = new ReviewAssertion("a", "reading", morphology, ReferenceAuthority.SourceValidated,
            "actor", "revision", false, false, true, []);
        var member = new ReviewMembership("ref", "case", "reference", 1, null, null, ["a"]);
        var item = Case("word", true, false, [member]) with
        { Assertions = [assertion], ReadingLosses = [new ReviewReadingLoss("reading", false, true, ["a"])] };
        var accumulator = new ReviewSummaryAccumulator();
        accumulator.Add(item);
        var control = Assert.Single(accumulator.Snapshot().Controls);
        Assert.Equal("reference", control.Role);
        Assert.Equal(1, control.SetCount);
        Assert.Equal(1, control.WordCount);
        Assert.Equal(1, control.ExpectedReadingCount);
        Assert.Equal(1, control.LostReadings);
        Assert.Null(control.AdditionalReadings);
    }

    [Fact]
    public void UnconfirmedNegativeIsUnknownAndPreexistingAcceptanceIsSeparate()
    {
        var confirmed = new ReviewAssertion("negative", "", null, ReferenceAuthority.Human,
            "actor", "revision", false, true, true, []);
        var accepted = new ReviewMembership("neg", "accepted", "negative", 1, null, null, ["negative"]);
        var unresolved = new ReviewMembership("neg", "unresolved", "negative", 1, null, null, []);
        var accumulator = new ReviewSummaryAccumulator();
        accumulator.Add(Case("accepted", true, false, [accepted]) with { Assertions = [confirmed] });
        accumulator.Add(Case("unresolved", false, true, [unresolved]));
        var result = accumulator.Snapshot().Negatives;
        Assert.Equal(1, result.SetCount);
        Assert.Equal(1, result.AlreadyAcceptedWords);
        Assert.Equal(1, result.UnknownWords);
        Assert.Equal(0, result.NewlyAcceptedWords);
    }

    [Fact]
    public void IncompleteTokenOverflowIsAnExplicitLimitation()
    {
        var accumulator = new ReviewSummaryAccumulator();
        accumulator.Add(Case("a", null, null, [Token("a", 9007199254740991L)]));
        accumulator.Add(Case("b", null, null, [Token("b", 1)]));
        var result = accumulator.Snapshot();
        Assert.Null(result.IncompleteTokenWeight);
        Assert.Contains("count-overflow", result.Limitations);
    }

    [Fact]
    public void CountOverflowNeverRounds()
    {
        var result = ReviewCoverage.Count([(true, 9007199254740991L), (false, 1L)]);
        Assert.Null(result.Denominator);
        Assert.Contains("count-overflow", result.ReasonCodes);
    }

    private static ReviewMembership Token(string id, long weight) =>
        new("tokens", id, "sampled-tokens", weight, null, null, []);
    private static ReviewCaseDelta CellCase(string word, string cell, string expected, bool matched)
    {
        var member = new ReviewMembership("cells", word + cell, "paradigm-cells", 1, cell, word, [expected]);
        return Case(word, true, true, [member]) with { After = Side(true, matched ? [expected] : [], matched ? [] : [expected]) };
    }
    private static ReviewCaseDelta Case(string key, bool? before, bool? after, IReadOnlyList<ReviewMembership> memberships) =>
        new(key, key, key, "ws", false, false, false, [], [], memberships, [])
        { Before = Side(before), After = Side(after) };
    private static ReviewCaseSideFacts Side(bool? parsed, IReadOnlyList<string>? matched = null, IReadOnlyList<string>? missing = null) =>
        new(parsed.HasValue ? Completion.Complete : Completion.NotMeasured, parsed, [], matched ?? [], missing ?? [], []);
}
