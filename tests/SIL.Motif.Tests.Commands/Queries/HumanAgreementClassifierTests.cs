using SIL.Motif.Host.Analysis;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using System.Globalization;
using Xunit;

namespace SIL.Motif.Tests.Commands.Queries;

public sealed class HumanAgreementClassifierTests
{
    private const string FormA = "11111111-1111-1111-1111-111111111111";
    private const string FormB = "22222222-2222-2222-2222-222222222222";
    private const string MsaA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string MsaB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private static readonly string IdA = CanonicalId.FromGuid(Guid.Parse("10000000-0000-0000-0000-000000000001")).Value;
    private static readonly string IdA2 = CanonicalId.FromGuid(Guid.Parse("10000000-0000-0000-0000-000000000002")).Value;
    private static readonly string IdB = CanonicalId.FromGuid(Guid.Parse("10000000-0000-0000-0000-000000000003")).Value;

    [Fact]
    public void CompleteEvidenceDistinguishesNoMatchUniqueAndAlternatives()
    {
        var noMatch = Compile(Decision(IdA, ReadingGrade.Approved, Morph(FormA))).Classify(Evidence(Analysis(FormB)));
        Assert.Equal(HumanAgreementApprovalState.NoApprovedMatch, noMatch.ApprovedState);
        Assert.Equal([IdA], noMatch.ProvenMissingApprovedAnalysisIds);

        var unique = Compile(Decision(IdA, ReadingGrade.Approved, Morph(FormA))).Classify(Evidence(Analysis(FormA)));
        Assert.Equal(HumanAgreementApprovalState.ApprovedUnique, unique.ApprovedState);
        Assert.Equal(1, unique.ApprovedExpectedMorphologyCount);
        Assert.Equal(1, unique.MatchedApprovedMorphologyCount);
        Assert.Equal([IdA], unique.MatchedApprovedAnalysisIds);

        var alternatives = Compile(
            Decision(IdA, ReadingGrade.Approved, Morph(FormA)),
            Decision(IdB, ReadingGrade.Approved, Morph(FormB)))
            .Classify(Evidence(Analysis(FormA), Analysis(FormB)));
        Assert.Equal(HumanAgreementApprovalState.ApprovedWithAlternatives, alternatives.ApprovedState);
        Assert.Equal(2, alternatives.MatchedApprovedMorphologyCount);
    }

    [Fact]
    public void ApprovalAlternativesCountDistinctProducedReadings()
    {
        var facts = Compile(Decision(IdA, ReadingGrade.Approved, Morph(FormA)))
            .Classify(Evidence(Analysis(FormA), Analysis(FormB)));

        Assert.Equal(2, facts.ProducedIdentityCount);
        Assert.Equal(1, facts.MatchedApprovedMorphologyCount);
        Assert.Equal(HumanAgreementApprovalState.ApprovedWithAlternatives, facts.ApprovedState);
    }

    [Fact]
    public void OneProducedReadingCanCoverMultipleLiteralConstraintsAndRemainUnique()
    {
        var facts = Compile(
            Decision(IdA, ReadingGrade.Approved, Morph(FormA, forms: ["form-a"])),
            Decision(IdB, ReadingGrade.Approved, Morph(FormA, forms: ["form-b"])))
            .Classify(Evidence(Analysis(FormA)));

        Assert.Equal(1, facts.ProducedIdentityCount);
        Assert.Equal(2, facts.MatchedApprovedMorphologyCount);
        Assert.Equal(HumanAgreementApprovalState.ApprovedUnique, facts.ApprovedState);
    }

    [Fact]
    public void CompleteEvidenceProvesOnlyTheApprovedReadingThatWasNotProducedMissing()
    {
        var facts = Compile(
            Decision(IdA, ReadingGrade.Approved, Morph(FormA)),
            Decision(IdB, ReadingGrade.Approved, Morph(FormB)))
            .Classify(Evidence(Analysis(FormA)));

        Assert.Equal(HumanAgreementApprovalState.ApprovedUnique, facts.ApprovedState);
        Assert.Equal(2, facts.ApprovedExpectedMorphologyCount);
        Assert.Equal(1, facts.MatchedApprovedMorphologyCount);
        Assert.Equal([IdB], facts.ObservedUnmatchedExpectedAnalysisIds);
        Assert.Equal([IdB], facts.ProvenMissingApprovedAnalysisIds);
    }

    [Fact]
    public void CappedEvidenceRetainsObservedMatchesButCannotProveAbsenceOrRejection()
    {
        var decisions = new[]
        {
            Decision(IdA, ReadingGrade.Approved, Morph(FormA)),
            Decision(IdB, ReadingGrade.Approved, Morph(FormB))
        };
        var observed = Compile(decisions).Classify(EvidenceCapped(Analysis(FormA)));
        Assert.Equal(HumanAgreementSearchCompletion.Incomplete, observed.Completion);
        Assert.Equal(HumanAgreementApprovalState.Incomplete, observed.ApprovedState);
        Assert.Equal([IdA], observed.MatchedApprovedAnalysisIds);
        Assert.Equal([IdB], observed.ObservedUnmatchedExpectedAnalysisIds);
        Assert.Empty(observed.ProvenMissingApprovedAnalysisIds);

        var empty = Compile([], CapturedSpellingStatus.Incorrect).Classify(EvidenceCapped());
        Assert.Equal(HumanAgreementSpellingAcceptance.Incomplete, empty.SpellingAcceptance);
        Assert.DoesNotContain(empty.NegativeEvidence, item => item.Kind == HumanAgreementNegativeEvidenceKind.MisspelledParse);
    }

    [Fact]
    public void IncorrectSpellingCanBeAcceptedByObservedPartialParseOrRejectedByCompleteEmptySearch()
    {
        var partial = Compile([], CapturedSpellingStatus.Incorrect).Classify(EvidenceCapped(Analysis(FormA)));
        Assert.Equal(HumanAgreementSpellingAcceptance.AcceptedMisspelling, partial.SpellingAcceptance);
        Assert.Contains(partial.NegativeEvidence, item => item.Kind == HumanAgreementNegativeEvidenceKind.MisspelledParse);

        var complete = Compile([], CapturedSpellingStatus.Incorrect).Classify(Evidence());
        Assert.Equal(HumanAgreementSpellingAcceptance.RejectedMisspelling, complete.SpellingAcceptance);
        Assert.DoesNotContain(complete.NegativeEvidence,
            item => item.Kind == HumanAgreementNegativeEvidenceKind.MisspelledParse);
    }

    [Fact]
    public void RefusedInvalidAndUnavailableEmptyEvidenceCannotRejectMisspelling()
    {
        var classifier = Compile([], CapturedSpellingStatus.Incorrect);
        var refused = Evidence() with { Refusal = new ParserRefusal("unavailable", "refused") };
        var invalid = Evidence() with { InvalidShape = true };
        var unavailable = Evidence() with { Unavailable = ["morphology projection failed"] };

        foreach (var evidence in new[] { refused, invalid, unavailable })
        {
            var facts = classifier.Classify(evidence);
            Assert.NotEqual(HumanAgreementSpellingAcceptance.RejectedMisspelling, facts.SpellingAcceptance);
            Assert.Equal(HumanAgreementApprovalState.Unavailable, facts.ApprovedState);
            Assert.Equal(HumanAgreementSpellingAcceptance.Unavailable, facts.SpellingAcceptance);
            Assert.DoesNotContain(facts.NegativeEvidence,
                item => item.Kind == HumanAgreementNegativeEvidenceKind.MisspelledParse);
        }
    }

    [Fact]
    public void MissingEvidenceIsNotMeasuredAndNoDecisionsStillReportsProducedMorphology()
    {
        var notMeasured = Compile([], CapturedSpellingStatus.Incorrect).Classify(null);
        Assert.Equal(HumanAgreementApprovalState.NotMeasured, notMeasured.ApprovedState);
        Assert.Equal(HumanAgreementSearchCompletion.NotMeasured, notMeasured.Completion);
        Assert.Equal(HumanAgreementSpellingAcceptance.Unavailable, notMeasured.SpellingAcceptance);

        var mechanical = Compile([]).Classify(Evidence(Analysis(FormA)));
        Assert.Equal(HumanAgreementApprovalState.NoApprovedExpectations, mechanical.ApprovedState);
        Assert.Equal(1, mechanical.ProducedIdentityCount);
        Assert.Single(mechanical.UnmatchedProducedIdentities);
    }

    [Fact]
    public void DuplicateParserIdentitiesDeduplicateAndDuplicateSourceWitnessesRemainDistinct()
    {
        var duplicates = Compile(
            Decision(IdA, ReadingGrade.Approved, Morph(FormA)),
            Decision(IdA2, ReadingGrade.Approved, Morph(FormA)))
            .Classify(Evidence(Analysis(FormA), Analysis(FormA)));

        Assert.Equal(1, duplicates.ProducedIdentityCount);
        Assert.Equal(1, duplicates.ApprovedExpectedMorphologyCount);
        Assert.Equal(1, duplicates.MatchedApprovedMorphologyCount);
        Assert.Equal([IdA, IdA2], duplicates.MatchedApprovedAnalysisIds);
    }

    [Fact]
    public void OpposedOpinionsAndIncorrectSpellingAreRetainedAsConflicts()
    {
        var opposed = Compile(
            Decision(IdA, ReadingGrade.Approved, Morph(FormA)),
            Decision(IdB, ReadingGrade.Disapproved, Morph(FormA)),
            Decision(IdA2, ReadingGrade.Candidate, Morph(FormA)))
            .Classify(Evidence(Analysis(FormA)));

        Assert.Equal(HumanAgreementApprovalState.ConflictingDecisions, opposed.ApprovedState);
        Assert.Equal([IdA], opposed.MatchedApprovedAnalysisIds);
        Assert.Equal([IdB], opposed.MatchedDisapprovedAnalysisIds);
        Assert.Equal([IdA2], opposed.MatchedUnknownAnalysisIds);
        Assert.Contains(opposed.NegativeEvidence, item => item.Kind == HumanAgreementNegativeEvidenceKind.DisapprovedParse);
        Assert.Contains(opposed.Conflicts, item => item.Kind == HumanAgreementConflictKind.OpposedOpinions);

        var spelling = Compile([Decision(IdA, ReadingGrade.Approved, Morph(FormA))], CapturedSpellingStatus.Incorrect)
            .Classify(Evidence(Analysis(FormA)));
        Assert.Equal(HumanAgreementSpellingAcceptance.AcceptedMisspelling, spelling.SpellingAcceptance);
        Assert.Contains(spelling.Conflicts, item => item.Kind == HumanAgreementConflictKind.ApprovedWithIncorrectSpelling);
    }

    [Fact]
    public void ASharedNonGuessedReadingConflictsAcrossDifferentLiteralConstraints()
    {
        var facts = Compile(
            Decision(IdA, ReadingGrade.Approved, Morph(FormA, forms: ["form-a"])),
            Decision(IdB, ReadingGrade.Disapproved, Morph(FormA, forms: ["form-b"])))
            .Classify(Evidence(Analysis(FormA)));

        Assert.Equal([IdA], facts.MatchedApprovedAnalysisIds);
        Assert.Equal([IdB], facts.MatchedDisapprovedAnalysisIds);
        Assert.Contains(facts.Conflicts, conflict => conflict.Kind == HumanAgreementConflictKind.OpposedOpinions &&
            conflict.AnalysisIds.SequenceEqual([IdA, IdB]));
    }

    [Fact]
    public void IdenticalOpposedExpectationsRemainAConflictWithoutAProducedReading()
    {
        var facts = Compile(
            Decision(IdA, ReadingGrade.Approved, Morph(FormA)),
            Decision(IdB, ReadingGrade.Disapproved, Morph(FormA)))
            .Classify(Evidence());

        Assert.Contains(facts.Conflicts, conflict => conflict.Kind == HumanAgreementConflictKind.OpposedOpinions &&
            conflict.AnalysisIds.SequenceEqual([IdA, IdB]));
    }

    [Fact]
    public void GuessedReadingCannotCreateConflictWithAnUnmatchedLiteralConstraint()
    {
        var facts = Compile(
            Decision(IdA, ReadingGrade.Approved, Morph(FormA, forms: ["form-a"])),
            Decision(IdB, ReadingGrade.Disapproved, Morph(FormA, forms: ["form-b"])))
            .Classify(Evidence(Analysis(FormA, guessed: "form-a")));

        Assert.Equal([IdA], facts.MatchedApprovedAnalysisIds);
        Assert.Empty(facts.MatchedDisapprovedAnalysisIds);
        Assert.DoesNotContain(facts.Conflicts,
            conflict => conflict.Kind == HumanAgreementConflictKind.OpposedOpinions);
    }

    [Fact]
    public void MorphologyIdentityIncludesMsaAndUsesTheDirectedGuessedStringRule()
    {
        var differentMsa = Compile(Decision(IdA, ReadingGrade.Approved, Morph(FormA, MsaA)))
            .Classify(Evidence(Analysis(FormA, MsaB)));
        Assert.Equal(HumanAgreementApprovalState.NoApprovedMatch, differentMsa.ApprovedState);

        var decomposed = "e\u0301";
        var composed = "\u00e9";
        var guessed = Compile(Decision(IdA, ReadingGrade.Approved, Morph(FormA, forms: [decomposed])))
            .Classify(Evidence(Analysis(FormA, guessed: composed)));
        Assert.Equal(HumanAgreementApprovalState.ApprovedUnique, guessed.ApprovedState);

        var directed = Compile(Decision(IdA, ReadingGrade.Approved, Morph(FormA, forms: [])))
            .Classify(Evidence(Analysis(FormA, guessed: composed)));
        Assert.Equal(HumanAgreementApprovalState.NoApprovedMatch, directed.ApprovedState);

        var noGuess = Compile(Decision(IdA, ReadingGrade.Approved, Morph(FormA, forms: [])))
            .Classify(Evidence(Analysis(FormA)));
        Assert.Equal(HumanAgreementApprovalState.ApprovedUnique, noGuess.ApprovedState);
    }

    [Fact]
    public void LiteralAlternativesArePartOfExpectedMorphologyIdentity()
    {
        var facts = Compile(
            Decision(IdA, ReadingGrade.Approved, Morph(FormA, forms: ["form-a"])),
            Decision(IdB, ReadingGrade.Approved, Morph(FormA, forms: ["form-b"])))
            .Classify(Evidence(Analysis(FormA, guessed: "form-a")));

        Assert.Equal(2, facts.ApprovedExpectedMorphologyCount);
        Assert.Equal(1, facts.MatchedApprovedMorphologyCount);
        Assert.Equal([IdA], facts.MatchedApprovedAnalysisIds);
        Assert.Equal([IdB], facts.ProvenMissingApprovedAnalysisIds);
    }

    [Fact]
    public void EmptyExpectedMorphologyIsUnavailableAndNeverMatchesAnEmptyReading()
    {
        var invalid = new ApprovedMorphology([]);
        var facts = Compile(Decision(IdA, ReadingGrade.Approved, invalid)).Classify(Evidence(Analysis(FormA)));

        Assert.Equal(HumanAgreementApprovalState.Unavailable, facts.ApprovedState);
        Assert.Contains(HumanAgreementLimitation.InvalidExpectedMorphology, facts.Limitations);
        Assert.Empty(facts.MatchedApprovedAnalysisIds);
        Assert.Empty(facts.ProvenMissingApprovedAnalysisIds);
    }

    [Fact]
    public void CompiledDecisionsOwnEveryInputList()
    {
        var forms = new List<string> { "form-a" };
        var morphs = new List<ApprovedMorph> { new(FormA, MsaA, null, forms) };
        var decisions = new List<HumanAnalysisDecision>
        {
            Decision(IdA, ReadingGrade.Approved, new ApprovedMorphology(morphs))
        };
        var compiled = HumanAgreementClassifier.Compile(decisions, CapturedSpellingStatus.Undecided);
        forms[0] = "changed";
        morphs.Clear();
        decisions.Clear();

        var facts = compiled.Classify(Evidence(Analysis(FormA)));
        Assert.Equal([IdA], facts.MatchedApprovedAnalysisIds);
    }

    [Fact]
    public void SourceAnalysisIdsMustDecodeToDistinctCanonicalGuids()
    {
        var invalid = Decision("not-a-canonical-id", ReadingGrade.Approved, Morph(FormA));
        Assert.Throws<ArgumentException>(() => Compile(invalid));

        var sameGuidWithPrefix = CanonicalId.FromGuid(
            Guid.Parse("10000000-0000-0000-0000-000000000001"), "analysis:").Value;
        Assert.Throws<ArgumentException>(() => Compile(
            Decision(IdA, ReadingGrade.Approved, Morph(FormA)),
            Decision(sameGuidWithPrefix, ReadingGrade.Approved, Morph(FormA))));
    }

    [Fact]
    public void MatchedWitnessPreservesItsSuppliedPortableIdPrefix()
    {
        var sourceId = CanonicalId.FromGuid(Guid.Parse("10000000-0000-0000-0000-000000000001"), "analysis:").Value;
        var facts = Compile(Decision(sourceId, ReadingGrade.Approved, Morph(FormA)))
            .Classify(Evidence(Analysis(FormA)));

        Assert.Equal([sourceId], facts.MatchedApprovedAnalysisIds);
    }

    [Fact]
    public void NegativeEvidenceWitnessListsAreFrozenAndCannotAlterLaterResults()
    {
        var compiled = Compile(Decision(IdB, ReadingGrade.Disapproved, Morph(FormA)));
        var first = compiled.Classify(Evidence(Analysis(FormA)));
        var witnessIds = first.NegativeEvidence.Single().AnalysisIds;

        Assert.Throws<NotSupportedException>(() => ((IList<string>)witnessIds)[0] = IdA);
        Assert.Equal([IdB], first.MatchedDisapprovedAnalysisIds);
        Assert.Equal([IdB], compiled.Classify(Evidence(Analysis(FormA))).NegativeEvidence.Single().AnalysisIds);
    }

    [Fact]
    public void IdentityKeysAreCultureIndependentAcrossCompilationAndClassification()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var compilingCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            compilingCulture.NumberFormat.NegativeSign = "~";
            CultureInfo.CurrentCulture = compilingCulture;
            var compiled = Compile(Decision(IdA, ReadingGrade.Approved, Morph(FormA, inflType: null)));

            var classifyingCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            classifyingCulture.NumberFormat.NegativeSign = "!";
            CultureInfo.CurrentCulture = classifyingCulture;
            var facts = compiled.Classify(Evidence(Analysis(FormA)));

            Assert.Equal(HumanAgreementApprovalState.ApprovedUnique, facts.ApprovedState);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void ReturnedUnmatchedIdentitiesOwnTheirParserMorphLists()
    {
        var morphs = new List<ParseMorph> { new(FormA, MsaA, null, "guessed") };
        var analyses = new List<ParseAnalysis> { new(morphs) };
        var evidence = new ParseWordEvidence("fieldworks-parse-analysis/v1", 0, "w", 1,
            false, false, false, analyses, []);
        var facts = Compile([]).Classify(evidence);

        morphs.Clear();
        analyses.Clear();

        Assert.Equal(1, facts.ProducedIdentityCount);
        Assert.Equal(FormA, facts.UnmatchedProducedIdentities[0].Morphs[0].Form);
        Assert.Equal("guessed", facts.UnmatchedProducedIdentities[0].Morphs[0].GuessedStringNfd);
        Assert.Throws<NotSupportedException>(() => ((IList<HumanAgreementProducedMorph>)
            facts.UnmatchedProducedIdentities[0].Morphs)[0] = new HumanAgreementProducedMorph(FormB, MsaA, null, null));
    }

    [Fact]
    public void InvalidParserMorphologyReturnsNamedUnavailableFacts()
    {
        var malformed = Evidence(new ParseAnalysis([]));
        var facts = Compile(Decision(IdA, ReadingGrade.Approved, Morph(FormA))).Classify(malformed);

        Assert.Equal(HumanAgreementEvidenceAvailability.Unavailable, facts.Availability);
        Assert.Equal(HumanAgreementApprovalState.Unavailable, facts.ApprovedState);
        Assert.Contains(HumanAgreementLimitation.MalformedEvidence, facts.Limitations);
        Assert.Empty(facts.ProvenMissingApprovedAnalysisIds);
    }

    [Fact]
    public void TimedOutEvidenceIsIncompleteAndCannotProveAbsence()
    {
        var evidence = Evidence(Analysis(FormA)) with { TimedOut = true };
        var facts = Compile(Decision(IdA, ReadingGrade.Approved, Morph(FormA)),
            Decision(IdB, ReadingGrade.Approved, Morph(FormB))).Classify(evidence);

        Assert.Equal(HumanAgreementSearchCompletion.Incomplete, facts.Completion);
        Assert.Equal(HumanAgreementApprovalState.Incomplete, facts.ApprovedState);
        Assert.Contains(HumanAgreementLimitation.TimedOut, facts.Limitations);
        Assert.Empty(facts.ProvenMissingApprovedAnalysisIds);
        Assert.Equal([IdA], facts.MatchedApprovedAnalysisIds);
    }

    [Fact]
    public void UnavailableProjectionKeepsObservedMatchesButReportsUnavailable()
    {
        var evidence = Evidence(Analysis(FormA)) with { Unavailable = ["morphology projection failed"] };
        var facts = Compile(Decision(IdA, ReadingGrade.Approved, Morph(FormA))).Classify(evidence);

        Assert.Equal(HumanAgreementApprovalState.Unavailable, facts.ApprovedState);
        Assert.Contains(HumanAgreementLimitation.ProjectionUnavailable, facts.Limitations);
        Assert.Equal([IdA], facts.MatchedApprovedAnalysisIds);
        Assert.Empty(facts.ProvenMissingApprovedAnalysisIds);
    }

    [Fact]
    public void CorrectAndUndecidedSpellingNeverCreateMisspellingEvidence()
    {
        foreach (var status in new[] { CapturedSpellingStatus.Correct, CapturedSpellingStatus.Undecided })
        {
            var facts = Compile([], status).Classify(Evidence(Analysis(FormA)));

            Assert.Equal(HumanAgreementSpellingAcceptance.NotApplicable, facts.SpellingAcceptance);
            Assert.DoesNotContain(facts.NegativeEvidence,
                item => item.Kind == HumanAgreementNegativeEvidenceKind.MisspelledParse);
        }
    }

    [Fact]
    public void ApprovedAndDisapprovedOnDifferentMorphologiesAreNotAConflict()
    {
        var facts = Compile(
            Decision(IdA, ReadingGrade.Approved, Morph(FormA)),
            Decision(IdB, ReadingGrade.Disapproved, Morph(FormB)))
            .Classify(Evidence(Analysis(FormA)));

        Assert.Equal(HumanAgreementApprovalState.ApprovedUnique, facts.ApprovedState);
        Assert.Empty(facts.Conflicts);
        Assert.Empty(facts.MatchedDisapprovedAnalysisIds);
    }

    [Fact]
    public void ADisapprovedMatchSurvivesCappedEvidenceAsNegativeEvidence()
    {
        var facts = Compile(Decision(IdB, ReadingGrade.Disapproved, Morph(FormA)))
            .Classify(EvidenceCapped(Analysis(FormA)));

        Assert.Equal(HumanAgreementSearchCompletion.Incomplete, facts.Completion);
        Assert.Equal([IdB], facts.MatchedDisapprovedAnalysisIds);
        Assert.Contains(facts.NegativeEvidence, item => item.Kind == HumanAgreementNegativeEvidenceKind.DisapprovedParse &&
            item.AnalysisIds.SequenceEqual([IdB]));
    }

    [Fact]
    public void NegativeAttemptsAndElapsedTimeAreMalformedEvidence()
    {
        var classifier = Compile(Decision(IdA, ReadingGrade.Approved, Morph(FormA)));
        foreach (var evidence in new[]
                 {
                     Evidence(Analysis(FormA)) with { Attempts = -1 },
                     Evidence(Analysis(FormA)) with { ElapsedMs = -1 }
                 })
        {
            var facts = classifier.Classify(evidence);

            Assert.Equal(HumanAgreementApprovalState.Unavailable, facts.ApprovedState);
            Assert.Contains(HumanAgreementLimitation.MalformedEvidence, facts.Limitations);
            Assert.Empty(facts.ProvenMissingApprovedAnalysisIds);
        }
    }

    [Fact]
    public void ARefusalWithABlankCodeIsMalformedEvidence()
    {
        var evidence = Evidence() with { Refusal = new ParserRefusal(" ", "refused") };
        var facts = Compile(Decision(IdA, ReadingGrade.Approved, Morph(FormA))).Classify(evidence);

        Assert.Equal(HumanAgreementApprovalState.Unavailable, facts.ApprovedState);
        Assert.Contains(HumanAgreementLimitation.MalformedEvidence, facts.Limitations);
    }

    [Fact]
    public void ANullDecisionAndANoOpinionAreRefusedAtCompile()
    {
        Assert.Throws<ArgumentException>(() => HumanAgreementClassifier.Compile(
            new HumanAnalysisDecision[] { null! }, CapturedSpellingStatus.Undecided));
        Assert.Throws<ArgumentException>(() => Compile(Decision(IdA, "no-opinion", Morph(FormA))));
    }

    [Fact]
    public void AWordWithoutApprovedDecisionsStillReportsItsObservedParseCount()
    {
        var parsed = Compile([]).Classify(Evidence(Analysis(FormA), Analysis(FormB)));
        var unparsed = Compile([]).Classify(Evidence());

        Assert.Equal(HumanAgreementApprovalState.NoApprovedExpectations, parsed.ApprovedState);
        Assert.Equal(2, parsed.ProducedIdentityCount);
        Assert.Equal(HumanAgreementApprovalState.NoApprovedExpectations, unparsed.ApprovedState);
        Assert.Equal(0, unparsed.ProducedIdentityCount);
    }

    private static CompiledHumanDecisions Compile(params HumanAnalysisDecision[] decisions) =>
        HumanAgreementClassifier.Compile(decisions, CapturedSpellingStatus.Undecided);

    private static CompiledHumanDecisions Compile(
        IReadOnlyList<HumanAnalysisDecision> decisions,
        CapturedSpellingStatus spelling) => HumanAgreementClassifier.Compile(decisions, spelling);

    private static HumanAnalysisDecision Decision(string id, string opinion, ApprovedMorphology morphology) =>
        new(id, opinion, morphology);

    private static ApprovedMorphology Morph(
        string form,
        string msa = MsaA,
        IReadOnlyList<string>? forms = null,
        string? inflType = null) =>
        new([new ApprovedMorph(form, msa, inflType, forms ?? ["form-a"])]);

    private static ParseAnalysis Analysis(string form, string msa = MsaA, string? guessed = null) =>
        new([new ParseMorph(form, msa, null, guessed)]);

    private static ParseWordEvidence Evidence(params ParseAnalysis[] analyses) =>
        new("fieldworks-parse-analysis/v1", 0, "w", 1, false, false, false, analyses, []);

    private static ParseWordEvidence EvidenceCapped(params ParseAnalysis[] analyses) =>
        new("fieldworks-parse-analysis/v1", 0, "w", 1, true, false, false, analyses, []);
}
