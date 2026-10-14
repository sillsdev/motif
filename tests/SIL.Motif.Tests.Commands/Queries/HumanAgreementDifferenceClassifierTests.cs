using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using Xunit;

namespace SIL.Motif.Tests.Commands.Queries;

public sealed class HumanAgreementDifferenceClassifierTests
{
    private const string FormA = "11111111-1111-1111-1111-111111111111";
    private const string FormB = "22222222-2222-2222-2222-222222222222";
    private const string MsaA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string MsaB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private static readonly string IdA = CanonicalId.FromGuid(Guid.Parse(FormA)).Value;
    private static readonly string IdB = CanonicalId.FromGuid(Guid.Parse(FormB)).Value;

    [Fact]
    public void LosingOneOfTwoApprovedReadingsIsRegressionAndNeverUniqueImprovement()
    {
        var facts = Compare(Evidence(A(), B()), Evidence(A()), Approved());
        Assert.Equal([IdB], Bucket(facts, ProposalDifferenceBucket.Regression).AnalysisIds);
        Assert.DoesNotContain(facts.Buckets, item => item.Bucket == ProposalDifferenceBucket.ImprovedUnique);
        Assert.True(facts.CouldBeBad);
        Assert.False(facts.Improved);
    }

    [Fact]
    public void NewAlternativeIsCompetingEvenWhenApprovedReadingSurvives()
    {
        var facts = Compare(Evidence(A()), Evidence(A(), B()), [Decision(IdA, ReadingGrade.Approved, FormA)]);
        Assert.Contains(facts.Buckets, item => item.Bucket == ProposalDifferenceBucket.NewCompetingParse);
        Assert.Equal(FormB, Assert.Single(facts.GainedIdentities).Morphs[0].Form);
        Assert.Empty(facts.LostIdentities);
    }

    [Fact]
    public void PartialAcceptanceCanEstablishNewOvergenerationOnlyFromCompleteBefore()
    {
        var facts = Compare(Evidence(), Evidence(B()) with { Capped = true }, [], CapturedSpellingStatus.Incorrect);
        Assert.True(Bucket(facts, ProposalDifferenceBucket.Overgeneration).ObservedAfterIncomplete);
        Assert.False(facts.CompleteComparison);
        Assert.Empty(facts.Changes);
        Assert.Empty(facts.GainedIdentities);

        var incompleteBefore = Compare(Evidence() with { Capped = true }, Evidence(B()), [], CapturedSpellingStatus.Incorrect);
        Assert.DoesNotContain(incompleteBefore.Buckets, item => item.Bucket == ProposalDifferenceBucket.Overgeneration);
    }

    [Theory]
    [InlineData(false, ProposalDifferenceBucket.Overgeneration)]
    [InlineData(true, ProposalDifferenceBucket.ImprovedNegativeRejection)]
    public void SurfaceSpellingBucketsIgnoreUnavailableUnknownAnalysisMorphology(bool acceptedBefore,
        ProposalDifferenceBucket expected)
    {
        var unknown = new HumanAnalysisDecision(IdA, ReadingGrade.Candidate, new ApprovedMorphology([]));
        var facts = Compare(acceptedBefore ? Evidence(B()) : Evidence(),
            acceptedBefore ? Evidence() : Evidence(B()), [unknown], CapturedSpellingStatus.Incorrect);
        Assert.Equal(HumanAgreementEvidenceAvailability.PartiallyAvailable, facts.Before.Availability);
        Assert.Equal(HumanAgreementEvidenceAvailability.PartiallyAvailable, facts.After.Availability);
        Assert.True(facts.CompleteComparison);
        Assert.False(Bucket(facts, expected).ObservedAfterIncomplete);
    }

    [Fact]
    public void PreviouslyAcceptedMisspellingDoesNotBecomeNewOvergeneration()
    {
        var facts = Compare(Evidence(A()), Evidence(A(), B()), [], CapturedSpellingStatus.Incorrect);
        Assert.DoesNotContain(facts.Buckets, item => item.Bucket == ProposalDifferenceBucket.Overgeneration);
        Assert.Equal(HumanAgreementSpellingAcceptance.AcceptedMisspelling, facts.After.SpellingAcceptance);
    }

    [Fact]
    public void NewlyObservedDisapprovedReadingRetainsItsWitnessUnderPartialAfter()
    {
        var decisions = new[] { Decision(IdB, ReadingGrade.Disapproved, FormB) };
        var facts = Compare(Evidence(A()), Evidence(A(), B()) with { TimedOut = true }, decisions);
        Assert.Equal([IdB], Bucket(facts, ProposalDifferenceBucket.NewWrongReading).AnalysisIds);
        Assert.True(Bucket(facts, ProposalDifferenceBucket.NewWrongReading).ObservedAfterIncomplete);
        var unknownBefore = Compare(Evidence(A()) with { Capped = true }, Evidence(B()), decisions);
        Assert.DoesNotContain(unknownBefore.Buckets, item => item.Bucket == ProposalDifferenceBucket.NewWrongReading);
    }

    [Fact]
    public void NewParseWithoutApprovedMatchIsAdvisoryOnlyInApprovedScope()
    {
        var approved = Compare(Evidence(), Evidence(B()), [Decision(IdA, ReadingGrade.Approved, FormA)]);
        Assert.Contains(approved.Buckets, item => item.Bucket == ProposalDifferenceBucket.NewWrongReading);
        var unknown = Compare(Evidence(), Evidence(B()), []);
        Assert.DoesNotContain(unknown.Buckets, item => item.Bucket == ProposalDifferenceBucket.NewWrongReading);
        Assert.Contains(ProposalDifferenceChange.Gained, unknown.Changes);
    }

    [Fact]
    public void ObservedApprovalGainFromProvenAbsenceCanBePartial()
    {
        var facts = Compare(Evidence(B()), Evidence(A()) with { Capped = true },
            [Decision(IdA, ReadingGrade.Approved, FormA)]);
        Assert.Equal([IdA], Bucket(facts, ProposalDifferenceBucket.ImprovedApprovedMatch).AnalysisIds);
        Assert.True(Bucket(facts, ProposalDifferenceBucket.ImprovedApprovedMatch).ObservedAfterIncomplete);
        Assert.True(facts.Improved);
        Assert.DoesNotContain(facts.Buckets, item => item.Bucket == ProposalDifferenceBucket.ImprovedUnique);
    }

    [Fact]
    public void ImprovementAndRegressionMayOverlapWithoutCountingUnionTwice()
    {
        var facts = Compare(Evidence(A()), Evidence(B()), Approved());
        Assert.Equal([IdA], Bucket(facts, ProposalDifferenceBucket.Regression).AnalysisIds);
        Assert.Equal([IdB], Bucket(facts, ProposalDifferenceBucket.ImprovedApprovedMatch).AnalysisIds);
        Assert.True(facts.CouldBeBad);
        Assert.True(facts.Improved);
        Assert.Equal(facts.Buckets.Count, facts.Buckets.Select(item => item.Bucket).Distinct().Count());
    }

    [Fact]
    public void RemovingOnlyUnapprovedAlternativeImprovesUnique()
    {
        var facts = Compare(Evidence(A(), B()), Evidence(A()), [Decision(IdA, ReadingGrade.Approved, FormA)]);
        Assert.Contains(facts.Buckets, item => item.Bucket == ProposalDifferenceBucket.ImprovedUnique);
        Assert.DoesNotContain(facts.Buckets, item => item.Bucket == ProposalDifferenceBucket.Regression);
    }

    [Fact]
    public void NegativeRejectionNeedsCompleteAfterAndRetainsDisapprovedWitness()
    {
        var decisions = new[] { Decision(IdB, ReadingGrade.Disapproved, FormB) };
        var facts = Compare(Evidence(B()), Evidence(), decisions);
        Assert.Equal([IdB], Bucket(facts, ProposalDifferenceBucket.ImprovedNegativeRejection).AnalysisIds);
        var observedBefore = Compare(Evidence(B()) with { Capped = true }, Evidence(), decisions);
        Assert.Equal([IdB], Bucket(observedBefore, ProposalDifferenceBucket.ImprovedNegativeRejection).AnalysisIds);
        var partial = Compare(Evidence(B()), Evidence() with { Capped = true }, decisions);
        Assert.DoesNotContain(partial.Buckets, item => item.Bucket == ProposalDifferenceBucket.ImprovedNegativeRejection);
        var spelling = Compare(Evidence(B()), Evidence(), [], CapturedSpellingStatus.Incorrect);
        Assert.Contains(spelling.Buckets, item => item.Bucket == ProposalDifferenceBucket.ImprovedNegativeRejection);
    }

    [Fact]
    public void MissingAfterStaysNotMeasuredAndCannotInheritBefore()
    {
        var facts = Compare(Evidence(A()), null, Approved());
        Assert.Equal(HumanAgreementEvidenceAvailability.NotMeasured, facts.After.Availability);
        Assert.Empty(facts.ObservedAfter);
        Assert.Empty(facts.Buckets);
        Assert.Empty(facts.LostIdentities);
    }

    [Fact]
    public void OpposedOpinionsKeepMechanicalChangeWithoutQualityClaims()
    {
        var decisions = new[] { Decision(IdA, ReadingGrade.Approved, FormA), Decision(IdB, ReadingGrade.Disapproved, FormA) };
        var facts = Compare(Evidence(A()), Evidence(B()), decisions);
        Assert.NotEmpty(facts.Before.Conflicts);
        Assert.Contains(ProposalDifferenceChange.Changed, facts.Changes);
        Assert.Equal([ProposalDifferenceBucket.AnalysisChanged], facts.Buckets.Select(item => item.Bucket));
    }

    [Theory]
    [InlineData(2, 4, false)]
    [InlineData(2, 5, true)]
    [InlineData(4, 7, false)]
    [InlineData(4, 8, true)]
    [InlineData(0, 2, false)]
    [InlineData(0, 3, true)]
    public void AmbiguityRequiresBothAbsoluteAndRatioThresholds(int before, int after, bool expected)
    {
        var facts = Compare(Evidence(Readings(before)), Evidence(Readings(after)), []);
        Assert.Equal(expected, facts.Buckets.Any(item => item.Bucket == ProposalDifferenceBucket.AmbiguityBlowup));
    }

    [Fact]
    public void ExactProducedIdentityIncludesMsaAndNormalizedGuessedString()
    {
        var changedMsa = Compare(Evidence(A()), Evidence(new ParseAnalysis([new ParseMorph(FormA, MsaB, null, null)])), []);
        Assert.Contains(ProposalDifferenceChange.Changed, changedMsa.Changes);
        Assert.Single(changedMsa.GainedIdentities);
        Assert.Single(changedMsa.LostIdentities);
        var normalized = Compare(Evidence(A("é")), Evidence(A("e\u0301")), []);
        Assert.Empty(normalized.Changes);
        var nullToGuess = Compare(Evidence(A()), Evidence(A("")), []);
        Assert.Contains(ProposalDifferenceChange.Changed, nullToGuess.Changes);
    }

    [Fact]
    public void PairOwnsAllInputAndOutputLists()
    {
        var morphs = new List<ParseMorph> { new(FormA, MsaA, null, null) };
        var analyses = new List<ParseAnalysis> { new(morphs) };
        var facts = Compare(Evidence(), Evidence() with { Analyses = analyses }, []);
        morphs.Clear();
        analyses.Clear();
        Assert.Equal(FormA, Assert.Single(facts.ObservedAfter).Morphs[0].Form);
        Assert.Throws<NotSupportedException>(() => ((IList<HumanAgreementProducedMorph>)facts.ObservedAfter[0].Morphs).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ProposalDifferenceBucketMembership>)facts.Buckets).Clear());
    }

    private static ProposalDifferenceBucketMembership Bucket(ProposalDifferenceCaseFacts facts, ProposalDifferenceBucket bucket) =>
        Assert.Single(facts.Buckets, item => item.Bucket == bucket);

    private static ProposalDifferenceCaseFacts Compare(ParseWordEvidence? before, ParseWordEvidence? after,
        IReadOnlyList<HumanAnalysisDecision> decisions, CapturedSpellingStatus spelling = CapturedSpellingStatus.Undecided) =>
        HumanAgreementDifferenceClassifier.Compare(before, after, HumanAgreementClassifier.Compile(decisions, spelling));

    private static HumanAnalysisDecision[] Approved() =>
        [Decision(IdA, ReadingGrade.Approved, FormA), Decision(IdB, ReadingGrade.Approved, FormB)];

    private static HumanAnalysisDecision Decision(string id, string opinion, string form) =>
        new(id, opinion, new ApprovedMorphology([new ApprovedMorph(form, MsaA, null, ["w"])]));

    private static ParseAnalysis A(string? guessed = null) => new([new ParseMorph(FormA, MsaA, null, guessed)]);
    private static ParseAnalysis B() => new([new ParseMorph(FormB, MsaA, null, null)]);
    private static ParseAnalysis[] Readings(int count) => Enumerable.Range(0, count).Select(index => A(index.ToString())).ToArray();
    private static ParseWordEvidence Evidence(params ParseAnalysis[] readings) =>
        new("fieldworks-parse-analysis/v1", 0, "w", 1, false, false, false, readings, []);
}
