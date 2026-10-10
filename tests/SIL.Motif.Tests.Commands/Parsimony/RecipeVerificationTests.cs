using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Commands.Parsimony;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Parser;
using Xunit;

namespace SIL.Motif.Tests.Commands.Parsimony;

public sealed class RecipeVerificationTests
{
    private static readonly string Digest = "sha256:" + new string('a', 64);
    private static readonly BaselineToken Baseline = new("project", Digest, "projection/v1",
        "2026-10-07T12:00:00Z", Digest);
    private static readonly Guid WordformGuid = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ReadingOneGuid = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid ReadingTwoGuid = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid FormOneGuid = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid FormTwoGuid = Guid.Parse("00000000-0000-0000-0000-000000000005");
    private static readonly Guid MsaGuid = Guid.Parse("00000000-0000-0000-0000-000000000006");
    private static readonly Guid NegativeCaseGuid = Guid.Parse("00000000-0000-0000-0000-000000000007");
    private static readonly Guid NegativeRevisionGuid = Guid.Parse("00000000-0000-0000-0000-000000000008");

    [Fact]
    public void FrozenExpectationManifestFixtureIsClosedAndReadable()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Parsimony", "Fixtures", "frozen-expectations.v1.json");

        var frozen = FrozenExpectationCodec.Parse(File.ReadAllText(path));

        Assert.Single(frozen.Cases);
        Assert.Equal("ara", frozen.Cases[0].Surface);
        Assert.Equal("approved", Assert.Single(frozen.Cases[0].Readings).Opinion);
        Assert.Equal("seh", frozen.Cases[0].WritingSystem);
        FrozenExpectationCodec.RequireComplete(frozen);
    }

    [Fact]
    public void PreservesEveryApprovedReadingUnderTheExistingOrderedIdentity()
    {
        var frozen = Expectations(Reading(FormOneGuid), Reading(FormTwoGuid));
        var readings = new[] { Parsed(FormOneGuid), Parsed(FormTwoGuid) };

        var result = RecipeVerification.Compare(frozen, Run("before", [Case("ara", readings)]),
            Run("after", [Case("ara", readings)]), RecipeVerificationCriteria.StructuralCleanup);

        Assert.Equal(RecipeVerificationStatus.Pass, result.Status);
        Assert.Equal(2, result.PreservedApprovedReadings);
        Assert.Equal(0, result.RecoveredApprovedReadings);
        Assert.Equal(0, result.LostApprovedReadings);
        Assert.Equal(0, result.ChangedAnalysisCases);
    }

    [Fact]
    public void SeparatesRecoveryFromAReadingLostByTheCandidate()
    {
        var frozen = Expectations(Reading(FormOneGuid), Reading(FormTwoGuid));

        var result = RecipeVerification.Compare(frozen,
            Run("before", [Case("ara", [Parsed(FormOneGuid)])]),
            Run("after", [Case("ara", [Parsed(FormOneGuid), Parsed(FormTwoGuid)])]),
            RecipeVerificationCriteria.Tightening);

        Assert.Equal(1, result.RecoveredApprovedReadings);
        Assert.Equal(0, result.LostApprovedReadings);
        Assert.Equal(RecipeVerificationStatus.Pass, result.Status);

        var lost = RecipeVerification.Compare(frozen,
            Run("before", [Case("ara", [Parsed(FormOneGuid), Parsed(FormTwoGuid)])]),
            Run("after", [Case("ara", [Parsed(FormOneGuid)])]),
            RecipeVerificationCriteria.Tightening);

        Assert.Equal(1, lost.PreservedApprovedReadings);
        Assert.Equal(1, lost.LostApprovedReadings);
        Assert.Equal(RecipeVerificationStatus.Regression, lost.Status);
    }

    [Fact]
    public void CandidateOutputCannotEraseTheFrozenBeforeExpectation()
    {
        var frozen = FrozenExpectationCodec.Parse(FrozenExpectationCodec.ToJson(Expectations(Reading(FormOneGuid))));
        var frozenForm = frozen.Cases[0].Readings[0].Morphs[0].Form;

        var result = RecipeVerification.Compare(frozen,
            Run("before", [Case("ara", [Parsed(FormOneGuid)])]),
            Run("after", [Case("ara", [])]), RecipeVerificationCriteria.StructuralCleanup);

        Assert.Equal(frozenForm, frozen.Cases[0].Readings[0].Morphs[0].Form);
        Assert.Equal(1, result.LostApprovedReadings);
        Assert.Equal(RecipeVerificationStatus.Regression, result.Status);
    }

    [Fact]
    public void V0RefusesAuthoredIdentityMappingsUntilTheirTranslationIsImplemented()
    {
        var mapping = new Dictionary<string, string> { [Id(FormOneGuid)] = Id(FormTwoGuid) };

        Assert.Throws<NotSupportedException>(() => RecipeVerification.Compare(Expectations(Reading(FormOneGuid)),
            Run("before", [Case("ara", [Parsed(FormOneGuid)])]),
            Run("after", [Case("ara", [Parsed(FormTwoGuid)])]), RecipeVerificationCriteria.Tightening, mapping));
    }

    [Fact]
    public void RetirementPairUsesTheOriginalBeforeAndTranslatedAfterMorphology()
    {
        var original = BindManifest(Expectations(Reading(FormOneGuid)));
        var reading = original.Cases[0].Readings[0];
        var translated = BindManifest(original with
        {
            Cases = [original.Cases[0] with
            {
                Readings = [reading with { Morphs = [reading.Morphs[0] with { Form = Id(FormTwoGuid) }] }],
            }],
        });
        var mapping = new RetirementExpectationMapping(Id(FormOneGuid), Id(MsaGuid), null,
            "whole", Id(FormTwoGuid));
        var translation = new RetirementExpectationTranslation(original, translated, [mapping],
            [new RetirementBundleTextEffect(Id(FormOneGuid), new Dictionary<string, string>(),
                new Dictionary<string, string>())], RetirementExpectationTranslationCodec.ComputeMappingDigest([mapping]),
            Digest, Digest, Digest);

        var result = RecipeVerification.Compare(translation,
            Run("before", [Case("ara", [Parsed(FormOneGuid)])]),
            Run("after", [Case("ara", [Parsed(FormTwoGuid)])]), RecipeVerificationCriteria.Tightening);

        Assert.Equal(RecipeVerificationStatus.Pass, result.Status);
        Assert.Equal(1, result.PreservedApprovedReadings);
        Assert.Equal(0, result.LostApprovedReadings);
    }

    [Fact]
    public void PartialNegativeWitnessDoesNotEnterTheCompletedAcceptanceRate()
    {
        var frozen = Expectations([], [new FrozenReviewedNegative(Id(NegativeCaseGuid), Id(NegativeRevisionGuid),
            Digest, "seh", "ara", "reviewed surface", "reading", [Morph(FormTwoGuid)])]);
        var partial = Case("ara", [Parsed(FormTwoGuid)], capped: true);

        var result = RecipeVerification.Compare(frozen,
            Run("before", [Case("ara", [])]), Run("after", [partial]), RecipeVerificationCriteria.Tightening);

        Assert.Equal(1, result.PartialNegativeWitnesses);
        Assert.Equal(0, result.CompletedNegativeCases);
        Assert.Equal(0, result.AcceptedNegativeCases);
        Assert.Equal(RecipeVerificationStatus.Regression, result.Status);
    }

    [Fact]
    public void AnIncompleteAfterSearchCannotClaimPositiveRecovery()
    {
        var frozen = Expectations(Reading(FormOneGuid));

        var result = RecipeVerification.Compare(frozen,
            Run("before", [Case("ara", [])]),
            Run("after", [Case("ara", [Parsed(FormOneGuid)], capped: true)]),
            RecipeVerificationCriteria.Tightening);

        Assert.Equal(0, result.RecoveredApprovedReadings);
        Assert.Equal(1, result.IncompleteCases);
        Assert.Equal(RecipeVerificationStatus.Incomplete, result.Status);
    }

    [Fact]
    public void IncompleteInputAndUnscopedCasesAreReportedWithTheEffectiveBudgets()
    {
        var frozen = Expectations(Reading(FormOneGuid)) with { Unavailable = ["unreadable project judgment"] };
        var options = Options(1000, 250_000, 1);
        var before = Run("before", [Case("ara", [Parsed(FormOneGuid)]), Case("extra", [])], options);
        var after = Run("after", [Case("ara", [Parsed(FormOneGuid)]), Case("extra", [])], options);

        var result = RecipeVerification.Compare(frozen, before, after, RecipeVerificationCriteria.Tightening);

        Assert.Equal(RecipeVerificationStatus.Incomplete, result.Status);
        Assert.Contains("unreadable project judgment", result.Unavailable);
        Assert.Equal(1, result.ExcludedAssessmentCases);
        Assert.Equal(1000, result.EffectiveOptions!.PerWordTimeoutMs);
        Assert.Equal(250_000, result.EffectiveOptions.PerWordStepLimit.Steps);
        Assert.Equal(1, result.EffectiveOptions.Threads);
        Assert.Equal(1, result.DistinctSurfaceCount);
        Assert.Equal(128, result.MaximumDistinctSurfaces);
    }

    [Fact]
    public void MismatchedAssessorKindOrOptionsCannotBePaired()
    {
        var frozen = Expectations(Reading(FormOneGuid));
        var before = Run("before", [Case("ara", [Parsed(FormOneGuid)])]);

        Assert.Throws<ComparisonRefusalException>(() => RecipeVerification.Compare(frozen, before,
            Run("other-assessor", [Case("ara", [Parsed(FormOneGuid)])], assessor: "other"),
            RecipeVerificationCriteria.Tightening));
        Assert.Throws<ComparisonRefusalException>(() => RecipeVerification.Compare(frozen, before,
            Run("other-kind", [Case("ara", [Parsed(FormOneGuid)])], kind: "Correctness"),
            RecipeVerificationCriteria.Tightening));
        Assert.Throws<RecipeVerificationRefusalException>(() => RecipeVerification.Compare(frozen, before,
            Run("other-options", [Case("ara", [Parsed(FormOneGuid)])], options: Options(2000, 200_000, 1)),
            RecipeVerificationCriteria.Tightening));
    }

    [Fact]
    public void DifferentConcreteSelectionsCannotBeTreatedAsAPairedRun()
    {
        var frozen = Expectations(Reading(FormOneGuid));

        Assert.Throws<RecipeVerificationRefusalException>(() => RecipeVerification.Compare(frozen,
            Run("before", [Case("ara", [Parsed(FormOneGuid)])]),
            Run("after", [Case("other", [Parsed(FormOneGuid)])]), RecipeVerificationCriteria.Tightening));
    }

    [Fact]
    public void CleanupRequiresEqualCompletedAnalysisSetsButTighteningReportsOnlyIntroducedNegatives()
    {
        var frozen = Expectations(Reading(FormOneGuid));
        var before = Run("before", [Case("ara", [Parsed(FormOneGuid), Parsed(FormTwoGuid)])]);
        var after = Run("after", [Case("ara", [Parsed(FormOneGuid)])]);

        var cleanup = RecipeVerification.Compare(frozen, before, after,
            RecipeVerificationCriteria.StructuralCleanup);
        var tightening = RecipeVerification.Compare(frozen, before, after, RecipeVerificationCriteria.Tightening);

        Assert.Equal(RecipeVerificationStatus.Regression, cleanup.Status);
        Assert.Equal(1, cleanup.ChangedAnalysisCases);
        Assert.Equal(RecipeVerificationStatus.Pass, tightening.Status);
    }

    private static FrozenExpectationSet Expectations(params FrozenExpectedReading[] readings) =>
        Expectations(readings, []);

    private static FrozenExpectationSet Expectations(IReadOnlyList<FrozenExpectedReading> readings,
        IReadOnlyList<FrozenReviewedNegative> negatives) => new(Baseline, Digest, Digest, "expectation-revision/v1",
        [new FrozenExpectationCase(Id(Guid.Parse("00000000-0000-0000-0000-000000000009")), Id(WordformGuid),
            "seh", "ara", true, false, readings)], negatives, []);

    private static FrozenExpectedReading Reading(Guid form) => new(Id(form), Id(form), "approved", Digest, [], [Morph(form)]);

    private static FrozenExpectationMorph Morph(Guid form) =>
        new(Id(form), Id(form), Id(MsaGuid), null, "whole", null, null, []);

    private static RecipeVerificationRun Run(string assessmentId, IReadOnlyList<AssessedWord> words,
        RecipeVerificationOptions? options = null, string assessor = "pangloss", string kind = "ParseTime") =>
        new(new ComparableAssessment(assessmentId, assessor, kind, "pangloss", "v7", words), Baseline,
            options ?? Options(), [], Selection.Create("verification", words.Select(word => word.Word)).Words);

    private static RecipeVerificationOptions Options(int timeoutMs = 1000, long steps = 200_000, int threads = 1) =>
        new("sha256:" + new string('b', 64), timeoutMs, new StepCap(steps), threads, true);

    private static FrozenExpectationSet BindManifest(FrozenExpectationSet frozen)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            cases = frozen.Cases.OrderBy(item => item.CaseId, StringComparer.Ordinal).ToArray(),
            reviewedNegatives = frozen.ReviewedNegatives.OrderBy(item => item.CaseId, StringComparer.Ordinal).ToArray(),
        }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var digest = IntentDigest.Sha256Of(CanonicalJson.CanonicalizeToUtf8(json));
        return frozen with { ManifestDigest = digest, ExpectationRevision = "frozen-expectations/v1/" + digest };
    }

    private static AssessedWord Case(string word, IReadOnlyList<ParseAnalysis> analyses, bool capped = false)
    {
        var morphology = new ParseWordEvidence("fieldworks-parse-analysis/v1", 0, word, 5,
            capped, false, false, analyses, []);
        return new AssessedWord(word, analyses.Count == 0 ? "no-analysis" : "analysed", [], 5)
        {
            Morphology = morphology,
            IsIncomplete = capped,
        };
    }

    private static ParseAnalysis Parsed(Guid form) => new([new ParseMorph(form.ToString("D"), MsaGuid.ToString("D"), null, null)]);

    private static string Id(Guid id) => CanonicalId.FromGuid(id).Value;
}
