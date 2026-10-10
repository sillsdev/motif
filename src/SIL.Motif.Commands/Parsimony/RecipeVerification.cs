using System.Text;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Parsimony;

namespace SIL.Motif.Commands.Parsimony;

/// <summary>The parse-preservation criteria appropriate to a recipe's purpose.</summary>
public enum RecipeVerificationCriteria
{
    /// <summary>Require equal completed parse-analysis sets on every required case.</summary>
    StructuralCleanup,

    /// <summary>Require preservation of produced Approved readings and no new negative acceptance.</summary>
    Tightening,
}

/// <summary>The outcome of comparing one frozen before expectation set with paired parser Assessments.</summary>
public enum RecipeVerificationStatus
{
    /// <summary>All required checks completed and the selected criteria were met.</summary>
    Pass,

    /// <summary>Completed evidence proves that the candidate changed a protected result.</summary>
    Regression,

    /// <summary>Some required evidence or phase did not complete, so no pass can be claimed.</summary>
    Incomplete,
}

/// <summary>Raised when parser Assessments do not form a valid pair for this verification.</summary>
public sealed class RecipeVerificationRefusalException(string message) : Exception(message);

/// <summary>The verification state of one frozen human reading.</summary>
public enum RecipeVerificationReadingStatus
{
    /// <summary>The parser produced the reading both before and after the recipe.</summary>
    Preserved,

    /// <summary>A completed before search missed the reading and the candidate produced it.</summary>
    Recovered,

    /// <summary>A completed before search already missed the reading.</summary>
    MissingBefore,

    /// <summary>A completed before search produced the reading and a completed after search did not.</summary>
    Lost,

    /// <summary>The before search was incomplete, but the candidate produced the reading.</summary>
    FoundAfterBeforeIncomplete,

    /// <summary>A required parser search or the expectation identity was unavailable.</summary>
    Incomplete,
}

/// <summary>One exact frozen reading's observed before and after presence.</summary>
public sealed record RecipeVerificationReading(
    string CaseId,
    string ReadingId,
    RecipeVerificationReadingStatus Status,
    bool? BeforeProduced,
    bool? AfterProduced,
    bool BeforeComplete,
    bool AfterComplete);

/// <summary>One reviewed negative or Disapproved reading checked against both parser results.</summary>
public sealed record RecipeVerificationNegative(
    string CaseId,
    string Target,
    bool IdentityAvailable,
    bool BeforeAccepted,
    bool AfterAccepted,
    bool BeforeComplete,
    bool AfterComplete,
    bool NewlyAccepted);

/// <summary>Counts, exclusions, limits and exact witnesses from paired recipe verification.</summary>
public sealed record RecipeVerificationResult(
    RecipeVerificationStatus Status,
    RecipeVerificationCriteria Criteria,
    int PreservedApprovedReadings,
    int RecoveredApprovedReadings,
    int MissingBeforeApprovedReadings,
    int LostApprovedReadings,
    int CompletedNegativeCases,
    int AcceptedNegativeCases,
    int NewlyAcceptedNegativeCases,
    int PartialNegativeWitnesses,
    int DistinctSurfaceCount,
    int MaximumDistinctSurfaces,
    int IncompleteCases,
    int ChangedAnalysisCases,
    int ExcludedAssessmentCases,
    int ExcludedExpectationReadings,
    bool NegativeEvidenceGap,
    RecipeVerificationOptions? EffectiveOptions,
    IReadOnlyList<RecipeVerificationReading> Readings,
    IReadOnlyList<RecipeVerificationNegative> Negatives,
    IReadOnlyList<string> Unavailable,
    IReadOnlyList<string> Exclusions);

/// <summary>Compares a candidate's completed parser outcomes with an independently frozen before oracle.</summary>
public static class RecipeVerification
{
    private const int MaximumDistinctSurfaces = 128;

    /// <summary>Runs the shared preservation and regression checks for one bounded grammar change.</summary>
    public static RecipeVerificationResult Compare(FrozenExpectationSet frozen,
        RecipeVerificationRun before, RecipeVerificationRun after, RecipeVerificationCriteria criteria,
        IReadOnlyDictionary<string, string>? identityMapping = null)
    {
        ArgumentNullException.ThrowIfNull(frozen);
        if (identityMapping is { Count: > 0 })
            throw new NotSupportedException("Explicit identity translation is not supported by frozen-expectations/v1 verification.");
        return ComparePair(frozen, frozen, before, after, criteria);
    }

    /// <summary>Compares the original oracle before and its Dry Run-bound translation after a retirement.</summary>
    public static RecipeVerificationResult Compare(RetirementExpectationTranslation translation,
        RecipeVerificationRun before, RecipeVerificationRun after, RecipeVerificationCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(translation);
        RetirementExpectationTranslator.ValidatePair(translation);
        return ComparePair(translation.Original, translation.Translated, before, after, criteria);
    }

    private static RecipeVerificationResult ComparePair(FrozenExpectationSet beforeFrozen,
        FrozenExpectationSet afterFrozen, RecipeVerificationRun before, RecipeVerificationRun after,
        RecipeVerificationCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(beforeFrozen);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        beforeFrozen = FrozenExpectationCodec.Parse(FrozenExpectationCodec.ToJson(beforeFrozen));
        afterFrozen = FrozenExpectationCodec.Parse(FrozenExpectationCodec.ToJson(afterFrozen));
        _ = AssessmentComparer.Compare(before.Assessment with { Words = [] },
            after.Assessment with { Words = [] });
        if (before.Assessment.Kind != "ParseTime")
            throw new ComparisonRefusalException("Paired verification requires two ParseTime Assessments.");
        if (!before.SelectionWords.SequenceEqual(after.SelectionWords, StringComparer.Ordinal))
            throw new RecipeVerificationRefusalException(
                "Both Assessments must use the same concrete word Selection.");
        if (before.Baseline != beforeFrozen.Baseline || after.Baseline != afterFrozen.Baseline)
            throw new RecipeVerificationRefusalException(
                "Both Assessments must name the Baseline bound by the frozen before expectations.");
        if (before.Assessment.TokeniserName != after.Assessment.TokeniserName ||
            before.Assessment.TokeniserVersion != after.Assessment.TokeniserVersion)
            throw new RecipeVerificationRefusalException("Both Assessments must use the same parser tokeniser options.");
        if (before.Options is not null && after.Options is not null && before.Options != after.Options)
            throw new RecipeVerificationRefusalException("Both Assessments must use the same parser executable and limits.");

        var unavailable = new List<string>(beforeFrozen.Unavailable);
        unavailable.AddRange(afterFrozen.Unavailable);
        unavailable.AddRange(before.Unavailable);
        unavailable.AddRange(after.Unavailable);
        if (before.Options is null || after.Options is null)
            unavailable.Add("A parser Assessment did not retain its effective executable and limits.");
        else if (before.Options.Threads < 1 || before.Options.PerWordTimeoutMs is <= 0 ||
                 before.Options.PerWordTimeoutMs > 1000 || before.Options.PerWordStepLimit.Steps is null or <= 0 or > 200_000)
            unavailable.Add("The parser limits do not satisfy the bounded verification contract.");
        if (beforeFrozen.Cases.Count == 0 && beforeFrozen.ReviewedNegatives.Count == 0)
            unavailable.Add("The frozen set contains no mandatory positive or negative verification cases.");

        var expectedSurfaces = beforeFrozen.Cases.Select(item => item.Surface.Normalize(NormalizationForm.FormD))
            .Concat(beforeFrozen.ReviewedNegatives.Select(item => item.Surface.Normalize(NormalizationForm.FormD)))
            .Concat(afterFrozen.Cases.Select(item => item.Surface.Normalize(NormalizationForm.FormD)))
            .Concat(afterFrozen.ReviewedNegatives.Select(item => item.Surface.Normalize(NormalizationForm.FormD)))
            .ToHashSet(StringComparer.Ordinal);
        if (expectedSurfaces.Count > MaximumDistinctSurfaces)
            unavailable.Add($"The frozen manifest has {expectedSurfaces.Count} distinct surfaces; the verification limit is {MaximumDistinctSurfaces}.");
        var beforeCases = Index(before.Assessment.Words, expectedSurfaces, "before", unavailable);
        var afterCases = Index(after.Assessment.Words, expectedSurfaces, "after", unavailable);
        var excludedCases = CountDistinctAssessmentCases(before.Assessment.Words, after.Assessment.Words, expectedSurfaces);
        var exclusions = new List<string>();
        if (excludedCases > 0) exclusions.Add($"{excludedCases} Assessment cases were outside the frozen manifest.");

        var readingResults = new List<RecipeVerificationReading>();
        var negativeResults = new List<RecipeVerificationNegative>();
        var incompleteCases = new HashSet<string>(StringComparer.Ordinal);
        var excludedReadings = 0;
        var beforeFrozenCases = beforeFrozen.Cases.ToDictionary(item => item.CaseId, StringComparer.Ordinal);
        var beforeFrozenNegatives = beforeFrozen.ReviewedNegatives.ToDictionary(item => item.CaseId, StringComparer.Ordinal);
        foreach (var item in afterFrozen.Cases)
        {
            var original = beforeFrozenCases[item.CaseId];
            var beforeSurface = original.Surface.Normalize(NormalizationForm.FormD);
            var afterSurface = item.Surface.Normalize(NormalizationForm.FormD);
            var beforeCase = Observe(beforeCases, beforeSurface, original.CaseId, "before", unavailable, incompleteCases);
            var afterCase = Observe(afterCases, afterSurface, item.CaseId, "after", unavailable, incompleteCases);
            var translatedReadings = item.Readings.ToDictionary(reading => reading.ReadingId, StringComparer.Ordinal);
            foreach (var reading in original.Readings)
            {
                if (reading.Opinion == "approved")
                {
                    readingResults.Add(CompareReading(item.CaseId, reading, translatedReadings[reading.ReadingId],
                        beforeCase, afterCase));
                }
                else if (reading.Opinion == "disapproved")
                {
                    if (!SupportedIdentity(reading.Morphs))
                    {
                        unavailable.Add($"Disapproved reading {reading.ReadingId} has incomplete identity.");
                        incompleteCases.Add(item.CaseId);
                    }
                    negativeResults.Add(CompareDisapproved(item.CaseId, reading,
                        translatedReadings[reading.ReadingId], beforeCase, afterCase));
                }
                else
                {
                    excludedReadings++;
                    exclusions.Add($"Reading {reading.ReadingId} has {reading.Opinion} Opinion and is not a positive or negative.");
                }
            }
        }

        foreach (var negative in afterFrozen.ReviewedNegatives)
        {
            var original = beforeFrozenNegatives[negative.CaseId];
            var identityAvailable = (original.Target == "surface" || SupportedIdentity(original.Morphs)) &&
                (negative.Target == "surface" || SupportedIdentity(negative.Morphs));
            if (!identityAvailable)
            {
                unavailable.Add($"Reviewed negative {negative.CaseId} has incomplete reading identity.");
                incompleteCases.Add(negative.CaseId);
            }
            var beforeSurface = original.Surface.Normalize(NormalizationForm.FormD);
            var afterSurface = negative.Surface.Normalize(NormalizationForm.FormD);
            var beforeCase = Observe(beforeCases, beforeSurface, negative.CaseId, "before", unavailable, incompleteCases);
            var afterCase = Observe(afterCases, afterSurface, negative.CaseId, "after", unavailable, incompleteCases);
            negativeResults.Add(CompareReviewedNegative(original, negative, beforeCase, afterCase, identityAvailable));
        }

        var completeWordPairs = SharedCompleteWords(expectedSurfaces, beforeCases, afterCases);
        var changedAnalysisCases = criteria == RecipeVerificationCriteria.StructuralCleanup
            ? CompareCompletedSets(before, after, completeWordPairs, unavailable)
            : 0;
        var lost = readingResults.Count(item => item.Status == RecipeVerificationReadingStatus.Lost);
        var preserved = readingResults.Count(item => item.Status == RecipeVerificationReadingStatus.Preserved);
        var recovered = readingResults.Count(item => item.Status == RecipeVerificationReadingStatus.Recovered);
        var missingBefore = readingResults.Count(item => item.Status == RecipeVerificationReadingStatus.MissingBefore);
        var completedNegatives = negativeResults.Count(item => item.IdentityAvailable &&
            item.BeforeComplete && item.AfterComplete);
        var acceptedNegatives = negativeResults.Count(item => item.IdentityAvailable &&
            item.BeforeComplete && item.AfterComplete && item.AfterAccepted);
        var newlyAccepted = negativeResults.Count(item => item.NewlyAccepted);
        var partialWitnesses = negativeResults.Count(item => !item.AfterComplete && item.AfterAccepted);
        var negativeGap = beforeFrozen.ReviewedNegatives.Count == 0 &&
            beforeFrozen.Cases.SelectMany(item => item.Readings).All(item => item.Opinion != "disapproved");
        if (negativeGap) exclusions.Add("No reviewed-negative or Disapproved-reading evidence was available.");

        var regression = lost > 0 || newlyAccepted > 0 || changedAnalysisCases > 0;
        var incomplete = incompleteCases.Count > 0 || unavailable.Count > 0 ||
            readingResults.Any(item => item.Status is RecipeVerificationReadingStatus.Incomplete or
                RecipeVerificationReadingStatus.FoundAfterBeforeIncomplete) ||
            negativeResults.Any(item => !item.IdentityAvailable || !item.BeforeComplete || !item.AfterComplete);
        var status = regression ? RecipeVerificationStatus.Regression
            : incomplete ? RecipeVerificationStatus.Incomplete
            : RecipeVerificationStatus.Pass;

        return new RecipeVerificationResult(status, criteria, preserved, recovered, missingBefore, lost,
            completedNegatives, acceptedNegatives, newlyAccepted, partialWitnesses, expectedSurfaces.Count,
            MaximumDistinctSurfaces, incompleteCases.Count,
            changedAnalysisCases, excludedCases, excludedReadings, negativeGap, after.Options,
            Array.AsReadOnly(readingResults.ToArray()), Array.AsReadOnly(negativeResults.ToArray()),
            Array.AsReadOnly(unavailable.Distinct(StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(exclusions.Distinct(StringComparer.Ordinal).ToArray()));
    }

    private static Dictionary<string, CaseObservation[]> Index(IReadOnlyList<AssessedWord> words,
        IReadOnlySet<string> expectedSurfaces, string phase, ICollection<string> unavailable)
    {
        var observations = words.GroupBy(item => Surface(item).Normalize(NormalizationForm.FormD), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(Observe).ToArray(), StringComparer.Ordinal);
        foreach (var surface in expectedSurfaces)
        {
            if (observations.TryGetValue(surface, out var rows) && rows.Length > 1)
                unavailable.Add($"The {phase} Assessment has multiple rows for required surface '{surface}'.");
        }
        return observations;
    }

    private static string Surface(AssessedWord word) => word.Morphology?.Word ?? word.Word;

    private static CaseObservation Observe(AssessedWord word)
    {
        var morphology = word.Morphology;
        var complete = morphology is not null && !word.IsIncomplete && !morphology.Capped &&
            !morphology.TimedOut && !morphology.InvalidShape && morphology.Unavailable.Count == 0 &&
            morphology.Word == word.Word;
        return new CaseObservation(word, complete);
    }

    private static CaseObservation? Observe(IReadOnlyDictionary<string, CaseObservation[]> index,
        string surface, string caseId, string phase, ICollection<string> unavailable,
        ISet<string> incompleteCases)
    {
        if (!index.TryGetValue(surface, out var rows) || rows.Length != 1)
        {
            incompleteCases.Add(caseId);
            unavailable.Add($"The {phase} Assessment does not contain one unambiguous row for case {caseId}.");
            return null;
        }
        if (!rows[0].Complete)
        {
            incompleteCases.Add(caseId);
            unavailable.Add($"The {phase} search for case {caseId} did not complete with resolved identities.");
        }
        return rows[0];
    }

    private static RecipeVerificationReading CompareReading(string caseId, FrozenExpectedReading beforeReading,
        FrozenExpectedReading afterReading, CaseObservation? before, CaseObservation? after)
    {
        var beforeMatch = Match(before, beforeReading.Morphs);
        var afterMatch = Match(after, afterReading.Morphs);
        var status = !SupportedIdentity(beforeReading.Morphs) || !SupportedIdentity(afterReading.Morphs) ||
            before is null || after is null
            ? RecipeVerificationReadingStatus.Incomplete
            : before.Complete && after.Complete
                ? beforeMatch && afterMatch ? RecipeVerificationReadingStatus.Preserved
                    : beforeMatch ? RecipeVerificationReadingStatus.Lost
                    : afterMatch ? RecipeVerificationReadingStatus.Recovered
                    : RecipeVerificationReadingStatus.MissingBefore
                : !before.Complete && after.Complete && afterMatch
                    ? RecipeVerificationReadingStatus.FoundAfterBeforeIncomplete
                    : RecipeVerificationReadingStatus.Incomplete;
        return new RecipeVerificationReading(caseId, beforeReading.ReadingId, status,
            before is null ? null : beforeMatch, after is null ? null : afterMatch,
            before?.Complete ?? false, after?.Complete ?? false);
    }

    private static RecipeVerificationNegative CompareDisapproved(string caseId, FrozenExpectedReading beforeReading,
        FrozenExpectedReading afterReading, CaseObservation? before, CaseObservation? after)
    {
        var beforeMatch = Match(before, beforeReading.Morphs);
        var afterMatch = Match(after, afterReading.Morphs);
        return Negative(caseId, "disapproved-reading",
            SupportedIdentity(beforeReading.Morphs) && SupportedIdentity(afterReading.Morphs),
            before, after, beforeMatch, afterMatch);
    }

    private static RecipeVerificationNegative CompareReviewedNegative(FrozenReviewedNegative beforeNegative,
        FrozenReviewedNegative afterNegative, CaseObservation? before, CaseObservation? after, bool identityAvailable)
    {
        var beforeMatch = MatchNegative(before, beforeNegative);
        var afterMatch = MatchNegative(after, afterNegative);
        var target = beforeNegative.Target == afterNegative.Target
            ? afterNegative.Target
            : beforeNegative.Target + "->" + afterNegative.Target;
        return Negative(afterNegative.CaseId, target, identityAvailable, before, after, beforeMatch, afterMatch);
    }

    private static RecipeVerificationNegative Negative(string caseId, string target,
        bool identityAvailable, CaseObservation? before, CaseObservation? after,
        bool beforeAccepted, bool afterAccepted)
    {
        var beforeComplete = before?.Complete ?? false;
        var afterComplete = after?.Complete ?? false;
        var newlyAccepted = identityAvailable && afterAccepted && beforeComplete && !beforeAccepted;
        return new RecipeVerificationNegative(caseId, target, identityAvailable, beforeAccepted, afterAccepted,
            beforeComplete, afterComplete, newlyAccepted);
    }

    private static bool MatchNegative(CaseObservation? observation, FrozenReviewedNegative negative)
    {
        if (observation?.Word.Morphology is not { } morphology) return false;
        if (negative.Target == "surface") return morphology.Analyses.Count > 0;
        return morphology.Analyses.Any(actual => Matches(actual, negative.Morphs));
    }

    private static bool Match(CaseObservation? observation, IReadOnlyList<FrozenExpectationMorph> morphs) =>
        observation?.Word.Morphology is { } morphology && morphology.Analyses.Any(actual => Matches(actual, morphs));

    private static bool Matches(ParseAnalysis actual, IReadOnlyList<FrozenExpectationMorph> expected)
    {
        if (!SupportedIdentity(expected)) return false;
        var target = new ApprovedMorphology(expected.Select(morph => new ApprovedMorph(
            GuidText(morph.Form), GuidText(morph.Msa), GuidText(morph.InflType),
            morph.BundleText.Select(item => item.Text).ToArray())).ToArray());
        return MorphologyCorrectness.Matches(actual, target);
    }

    private static bool SupportedIdentity(IReadOnlyList<FrozenExpectationMorph> morphs) =>
        morphs.Count > 0 && morphs.All(item => item.Form is not null && item.Msa is not null);

    private static string? GuidText(string? id) => id is null
        ? null
        : CanonicalId.Parse(id).ToGuid().ToString("D").ToLowerInvariant();

    private static IReadOnlyList<(string Surface, AssessedWord Before, AssessedWord After)> SharedCompleteWords(
        IReadOnlySet<string> expectedSurfaces,
        IReadOnlyDictionary<string, CaseObservation[]> beforeCases,
        IReadOnlyDictionary<string, CaseObservation[]> afterCases)
    {
        var result = new List<(string, AssessedWord, AssessedWord)>();
        foreach (var surface in expectedSurfaces)
        {
            if (!beforeCases.TryGetValue(surface, out var from) || from.Length != 1 || !from[0].Complete ||
                !afterCases.TryGetValue(surface, out var to) || to.Length != 1 || !to[0].Complete) continue;
            result.Add((surface, Normalize(from[0].Word), Normalize(to[0].Word)));
        }
        return result;
    }

    private static AssessedWord Normalize(AssessedWord word)
    {
        var surface = word.Word.Normalize(NormalizationForm.FormD);
        var morphology = word.Morphology is null ? null : word.Morphology with { Word = surface };
        return word with { Word = surface, Morphology = morphology };
    }

    private static int CompareCompletedSets(RecipeVerificationRun before, RecipeVerificationRun after,
        IReadOnlyList<(string Surface, AssessedWord Before, AssessedWord After)> pairs,
        ICollection<string> unavailable)
    {
        try
        {
            var comparison = AssessmentComparer.Compare(
                before.Assessment with { Words = pairs.Select(item => item.Before).ToArray() },
                after.Assessment with { Words = pairs.Select(item => item.After).ToArray() });
            return comparison.Changes.Count;
        }
        catch (ComparisonRefusalException exception)
        {
            unavailable.Add("Completed analysis sets could not be compared: " + exception.Message);
            return 0;
        }
    }

    private static int CountDistinctAssessmentCases(IReadOnlyList<AssessedWord> before,
        IReadOnlyList<AssessedWord> after, IReadOnlySet<string> expectedSurfaces) =>
        before.Concat(after).Select(item => Surface(item).Normalize(NormalizationForm.FormD))
            .Where(item => !expectedSurfaces.Contains(item)).Distinct(StringComparer.Ordinal).Count();

    private sealed record CaseObservation(AssessedWord Word, bool Complete);
}
