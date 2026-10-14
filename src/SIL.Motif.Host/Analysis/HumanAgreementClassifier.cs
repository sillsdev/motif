using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using DecisionEntry = SIL.Motif.Host.Analysis.HumanAgreementClassifier.DecisionEntry;
using BaseIndex = SIL.Motif.Host.Analysis.HumanAgreementClassifier.BaseIndex;
using ValidatedEvidence = SIL.Motif.Host.Analysis.HumanAgreementClassifier.ValidatedEvidence;

namespace SIL.Motif.Host.Analysis;

/// <summary>Compiles captured human decisions for comparison with parser morphology.</summary>
public static class HumanAgreementClassifier
{
    private const string ParseSchema = "fieldworks-parse-analysis/v1";

    /// <summary>Creates an immutable snapshot of captured decisions.</summary>
    public static CompiledHumanDecisions Compile(
        IReadOnlyList<HumanAnalysisDecision> decisions,
        CapturedSpellingStatus spelling)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        if (!Enum.IsDefined(spelling)) throw new ArgumentOutOfRangeException(nameof(spelling));

        var seenIds = new HashSet<Guid>();
        var entries = new List<DecisionEntry>(decisions.Count);
        var hasInvalidMorphology = false;
        foreach (var decision in decisions)
        {
            if (decision is null) throw new ArgumentException("A human decision cannot be null.", nameof(decisions));
            if (!CanonicalId.TryParse(decision.AnalysisId, out var canonicalId))
                throw new ArgumentException("A source analysis ID must encode a canonical nonempty GUID.", nameof(decisions));
            var sourceGuid = canonicalId.ToGuid();
            if (sourceGuid == Guid.Empty)
                throw new ArgumentException("A source analysis ID must encode a canonical nonempty GUID.", nameof(decisions));
            if (!seenIds.Add(sourceGuid))
                throw new ArgumentException("A source analysis ID can appear only once.", nameof(decisions));

            var opinion = decision.Opinion switch
            {
                ReadingGrade.Approved => HumanAgreementOpinion.Approved,
                ReadingGrade.Disapproved => HumanAgreementOpinion.Disapproved,
                ReadingGrade.Candidate => HumanAgreementOpinion.Unknown,
                _ => throw new ArgumentException("A human decision Opinion must be approved, disapproved, or candidate.",
                    nameof(decisions))
            };

            if (!TryCopyMorphology(decision.Morphology, out var morphology, out var morphologyKey, out var baseKey))
            {
                hasInvalidMorphology = true;
                entries.Add(new DecisionEntry(decision.AnalysisId, opinion, null, null, null));
                continue;
            }

            entries.Add(new DecisionEntry(decision.AnalysisId, opinion, morphology, morphologyKey, baseKey));
        }

        return new CompiledHumanDecisions(entries.ToArray(), spelling, hasInvalidMorphology);
    }

    private static bool TryCopyMorphology(
        ApprovedMorphology? source,
        out ApprovedMorphology? copy,
        out string? morphologyKey,
        out string? baseKey)
    {
        copy = null;
        morphologyKey = null;
        baseKey = null;
        if (source?.Morphs is null || source.Morphs.Count == 0 ||
            (source.SourceWordformGuid is not null && !IsCanonicalGuid(source.SourceWordformGuid))) return false;

        var morphs = new ApprovedMorph[source.Morphs.Count];
        for (var index = 0; index < source.Morphs.Count; index++)
        {
            var morph = source.Morphs[index];
            if (morph?.Forms is null || morph.Forms.Any(form => form is null) ||
                !IsCanonicalGuid(morph.Form) || !IsCanonicalGuid(morph.Msa) ||
                (morph.InflType is not null && !IsCanonicalGuid(morph.InflType))) return false;

            string[] normalizedForms;
            try
            {
                normalizedForms = morph.Forms.Select(Nfd).Distinct(StringComparer.Ordinal)
                    .OrderBy(form => form, StringComparer.Ordinal).ToArray();
            }
            catch (ArgumentException)
            {
                return false;
            }

            morphs[index] = new ApprovedMorph(morph.Form, morph.Msa, morph.InflType,
                Array.AsReadOnly(morph.Forms.ToArray()));
        }

        copy = new ApprovedMorphology(Array.AsReadOnly(morphs))
        {
            SourceWordformGuid = source.SourceWordformGuid,
            WritingSystem = source.WritingSystem
        };
        baseKey = BaseKey(copy);
        morphologyKey = ExpectedKey(copy, morphs.Select(morph => morph.Forms.Select(Nfd)
            .Distinct(StringComparer.Ordinal).OrderBy(form => form, StringComparer.Ordinal).ToArray()).ToArray());
        return true;
    }

    private static string ExpectedKey(ApprovedMorphology morphology, IReadOnlyList<string[]> normalizedForms)
    {
        var builder = new StringBuilder(BaseKey(morphology));
        for (var index = 0; index < normalizedForms.Count; index++)
        {
            builder.Append(normalizedForms[index].Length.ToString(CultureInfo.InvariantCulture)).Append(';');
            foreach (var form in normalizedForms[index]) AppendPart(builder, form);
        }
        return builder.ToString();
    }

    private static bool IsCanonicalGuid(string? value) =>
        value is not null && Guid.TryParseExact(value, "D", out var guid) && guid != Guid.Empty &&
        string.Equals(guid.ToString("D", CultureInfo.InvariantCulture), value, StringComparison.Ordinal);

    private static string BaseKey(ApprovedMorphology morphology)
    {
        var builder = new StringBuilder();
        builder.Append(morphology.Morphs.Count.ToString(CultureInfo.InvariantCulture)).Append(';');
        foreach (var morph in morphology.Morphs)
        {
            AppendPart(builder, morph.Form);
            AppendPart(builder, morph.Msa);
            AppendPart(builder, morph.InflType);
        }
        return builder.ToString();
    }

    internal static string BaseKey(ParseAnalysis analysis)
    {
        var builder = new StringBuilder();
        builder.Append(analysis.Morphs.Count.ToString(CultureInfo.InvariantCulture)).Append(';');
        foreach (var morph in analysis.Morphs)
        {
            AppendPart(builder, morph.Form);
            AppendPart(builder, morph.Msa);
            AppendPart(builder, morph.InflType);
        }
        return builder.ToString();
    }

    internal static string ParserIdentityKey(ParseAnalysis analysis)
    {
        var builder = new StringBuilder(BaseKey(analysis));
        foreach (var morph in analysis.Morphs) AppendPart(builder, morph.GuessedString is null ? null : Nfd(morph.GuessedString));
        return builder.ToString();
    }

    private static void AppendPart(StringBuilder builder, string? value)
    {
        builder.Append((value?.Length ?? -1).ToString(CultureInfo.InvariantCulture)).Append(':');
        if (value is not null) builder.Append(value);
        builder.Append(';');
    }

    internal static string Nfd(string value) => value.Normalize(NormalizationForm.FormD);

    internal sealed class DecisionEntry(
        string analysisId,
        HumanAgreementOpinion opinion,
        ApprovedMorphology? morphology,
        string? morphologyKey,
        string? baseKey)
    {
        public string AnalysisId { get; } = analysisId;
        public HumanAgreementOpinion Opinion { get; } = opinion;
        public ApprovedMorphology? Morphology { get; } = morphology;
        public string? MorphologyKey { get; } = morphologyKey;
        public string? BaseKey { get; } = baseKey;
    }

    internal sealed class BaseIndex
    {
        public List<DecisionEntry> Decisions { get; } = [];
        public Dictionary<int, Dictionary<string, HashSet<DecisionEntry>>> GuessedAlternatives { get; } = [];

        public void Add(DecisionEntry entry)
        {
            Decisions.Add(entry);
            var morphology = entry.Morphology!;
            for (var position = 0; position < morphology.Morphs.Count; position++)
            {
                if (!GuessedAlternatives.TryGetValue(position, out var byForm))
                {
                    byForm = new Dictionary<string, HashSet<DecisionEntry>>(StringComparer.Ordinal);
                    GuessedAlternatives.Add(position, byForm);
                }

                foreach (var form in morphology.Morphs[position].Forms.Select(Nfd).Distinct(StringComparer.Ordinal))
                {
                    if (!byForm.TryGetValue(form, out var candidates))
                    {
                        candidates = [];
                        byForm.Add(form, candidates);
                    }
                    candidates.Add(entry);
                }
            }
        }

        public IReadOnlyCollection<DecisionEntry> Candidates(ParseAnalysis actual)
        {
            HashSet<DecisionEntry>? candidates = null;
            for (var position = 0; position < actual.Morphs.Count; position++)
            {
                var guessed = actual.Morphs[position].GuessedString;
                if (guessed is null) continue;
                var normalized = Nfd(guessed);
                if (!GuessedAlternatives.TryGetValue(position, out var byForm) || !byForm.TryGetValue(normalized, out var matching))
                    return Array.Empty<DecisionEntry>();
                if (candidates is null) candidates = new HashSet<DecisionEntry>(matching);
                else candidates.IntersectWith(matching);
                if (candidates.Count == 0) return Array.Empty<DecisionEntry>();
            }
            return candidates is null ? Decisions : candidates;
        }
    }

    internal sealed record ValidatedEvidence(
        HumanAgreementEvidenceAvailability Availability,
        HumanAgreementSearchCompletion Completion,
        HumanAgreementLimitation[] Limitations,
        ParseWordEvidence? Evidence,
        bool CanClassifyMorphology,
        bool CanProveMissing,
        bool CanProveSearchEmpty);

    internal static ValidatedEvidence Validate(ParseWordEvidence? evidence, bool invalidExpectedMorphology)
    {
        if (evidence is null)
            return new ValidatedEvidence(HumanAgreementEvidenceAvailability.NotMeasured,
                HumanAgreementSearchCompletion.NotMeasured,
                invalidExpectedMorphology ? [HumanAgreementLimitation.InvalidExpectedMorphology] : [],
                null, false, false, false);

        var limitations = new HashSet<HumanAgreementLimitation>();
        if (invalidExpectedMorphology) limitations.Add(HumanAgreementLimitation.InvalidExpectedMorphology);
        if (evidence.Schema != ParseSchema || evidence.ElapsedMs < 0 || evidence.Attempts is < 0 ||
            evidence.Analyses is null || evidence.Unavailable is null)
        {
            limitations.Add(HumanAgreementLimitation.MalformedEvidence);
            return Unavailable(limitations);
        }

        if (evidence.InvalidShape)
        {
            limitations.Add(HumanAgreementLimitation.InvalidShape);
            if (evidence.Capped || evidence.TimedOut || evidence.Analyses.Count != 0 || evidence.Unavailable.Count != 0)
                limitations.Add(HumanAgreementLimitation.MalformedEvidence);
            return Unavailable(limitations);
        }

        if (evidence.Refusal is not null)
        {
            limitations.Add(HumanAgreementLimitation.ParserRefused);
            if (string.IsNullOrWhiteSpace(evidence.Refusal.Code) || string.IsNullOrWhiteSpace(evidence.Refusal.Reason) ||
                evidence.Analyses.Count != 0 || evidence.Capped || evidence.TimedOut || evidence.Unavailable.Count != 0)
                limitations.Add(HumanAgreementLimitation.MalformedEvidence);
            return Unavailable(limitations);
        }

        if (evidence.Unavailable.Any(string.IsNullOrWhiteSpace) || !ValidateAnalyses(evidence.Analyses))
        {
            limitations.Add(HumanAgreementLimitation.MalformedEvidence);
            return Unavailable(limitations);
        }

        if (evidence.Capped) limitations.Add(HumanAgreementLimitation.Capped);
        if (evidence.TimedOut) limitations.Add(HumanAgreementLimitation.TimedOut);
        if (evidence.Unavailable.Count > 0) limitations.Add(HumanAgreementLimitation.ProjectionUnavailable);

        var availability = invalidExpectedMorphology || evidence.Unavailable.Count > 0
            ? HumanAgreementEvidenceAvailability.PartiallyAvailable
            : HumanAgreementEvidenceAvailability.Available;
        var completion = evidence.Capped || evidence.TimedOut
            ? HumanAgreementSearchCompletion.Incomplete
            : HumanAgreementSearchCompletion.Complete;
        var canProveMissing = availability == HumanAgreementEvidenceAvailability.Available &&
            completion == HumanAgreementSearchCompletion.Complete;
        var canProveSearchEmpty = evidence.Unavailable.Count == 0 &&
            completion == HumanAgreementSearchCompletion.Complete;
        return new ValidatedEvidence(availability, completion, OrderedLimitations(limitations), evidence, true,
            canProveMissing, canProveSearchEmpty);

        static ValidatedEvidence Unavailable(HashSet<HumanAgreementLimitation> reasons) => new(
            HumanAgreementEvidenceAvailability.Unavailable,
            HumanAgreementSearchCompletion.Unavailable,
            OrderedLimitations(reasons),
            null,
            false,
            false,
            false);
    }

    private static bool ValidateAnalyses(IReadOnlyList<ParseAnalysis> analyses)
    {
        foreach (var analysis in analyses)
        {
            if (analysis?.Morphs is null || analysis.Morphs.Count == 0) return false;
            foreach (var morph in analysis.Morphs)
            {
                if (morph is null || !IsCanonicalGuid(morph.Form) || !IsCanonicalGuid(morph.Msa) ||
                    (morph.InflType is not null && !IsCanonicalGuid(morph.InflType))) return false;
                if (morph.GuessedString is not null)
                {
                    try { _ = Nfd(morph.GuessedString); }
                    catch (ArgumentException) { return false; }
                }
            }
        }
        return true;
    }

    private static HumanAgreementLimitation[] OrderedLimitations(HashSet<HumanAgreementLimitation> limitations) =>
        Enum.GetValues<HumanAgreementLimitation>().Where(limitations.Contains).ToArray();

    internal static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
}

/// <summary>Classifies parser evidence against one immutable capture of human decisions.</summary>
public sealed class CompiledHumanDecisions
{
    private readonly DecisionEntry[] _decisions;
    private readonly CapturedSpellingStatus _spelling;
    private readonly bool _hasInvalidMorphology;
    private readonly Dictionary<string, BaseIndex> _byBaseKey;
    private readonly Dictionary<string, DecisionEntry[]> _byMorphologyKey;
    private readonly HashSet<string> _approvedMorphologyKeys;
    private readonly HumanAgreementConflict[] _decisionConflicts;

    internal CompiledHumanDecisions(DecisionEntry[] decisions, CapturedSpellingStatus spelling, bool hasInvalidMorphology)
    {
        _decisions = decisions;
        _spelling = spelling;
        _hasInvalidMorphology = hasInvalidMorphology;
        _byBaseKey = new Dictionary<string, BaseIndex>(StringComparer.Ordinal);
        _byMorphologyKey = decisions.Where(decision => decision.MorphologyKey is not null)
            .GroupBy(decision => decision.MorphologyKey!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        _approvedMorphologyKeys = decisions.Where(decision => decision.Opinion == HumanAgreementOpinion.Approved &&
                decision.MorphologyKey is not null)
            .Select(decision => decision.MorphologyKey!).ToHashSet(StringComparer.Ordinal);

        foreach (var decision in decisions.Where(decision => decision.Morphology is not null))
        {
            if (!_byBaseKey.TryGetValue(decision.BaseKey!, out var index))
            {
                index = new BaseIndex();
                _byBaseKey.Add(decision.BaseKey!, index);
            }
            index.Add(decision);
        }

        _decisionConflicts = BuildDecisionConflicts(_byMorphologyKey);
    }

    /// <summary>Returns agreement facts for one parser result.</summary>
    public HumanAgreementFacts Classify(ParseWordEvidence? evidence)
    {
        var validated = HumanAgreementClassifier.Validate(evidence, _hasInvalidMorphology);
        if (!validated.CanClassifyMorphology)
            return CreateUnavailableFacts(validated);

        var seen = new SortedDictionary<string, ParseAnalysis>(StringComparer.Ordinal);
        foreach (var analysis in validated.Evidence!.Analyses)
            seen.TryAdd(HumanAgreementClassifier.ParserIdentityKey(analysis), analysis);

        var matchedDecisions = new HashSet<DecisionEntry>();
        var matchedApprovedMorphologies = new HashSet<string>(StringComparer.Ordinal);
        var unmatchedProduced = new List<HumanAgreementProducedIdentity>();
        var observedConflicts = new List<HumanAgreementConflict>();
        foreach (var (identityKey, actual) in seen)
        {
            var matchedAnyDecision = false;
            var matchedForProduced = new HashSet<DecisionEntry>();
            if (_byBaseKey.TryGetValue(HumanAgreementClassifier.BaseKey(actual), out var candidates))
            {
                foreach (var candidate in candidates.Candidates(actual))
                {
                    if (!AnalysisMorphologyMatcher.Matches(actual, candidate.Morphology!)) continue;
                    matchedAnyDecision = true;
                    matchedDecisions.Add(candidate);
                    matchedForProduced.Add(candidate);
                    if (candidate.Opinion == HumanAgreementOpinion.Approved)
                        matchedApprovedMorphologies.Add(candidate.MorphologyKey!);
                }
            }

            if (!matchedAnyDecision) unmatchedProduced.Add(ProducedIdentity(actual));
            var observedConflict = ObservedDecisionConflict(matchedForProduced);
            if (observedConflict is not null) observedConflicts.Add(observedConflict);
        }

        var matchedApprovedIds = IdsFor(matchedDecisions, HumanAgreementOpinion.Approved);
        var matchedDisapprovedIds = IdsFor(matchedDecisions, HumanAgreementOpinion.Disapproved);
        var matchedUnknownIds = IdsFor(matchedDecisions, HumanAgreementOpinion.Unknown);
        var unmatchedExpectedIds = _decisions.Where(decision => !matchedDecisions.Contains(decision))
            .Select(decision => decision.AnalysisId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var provenMissingIds = validated.CanProveMissing
            ? _decisions.Where(decision => decision.Opinion == HumanAgreementOpinion.Approved &&
                    !matchedDecisions.Contains(decision))
                .Select(decision => decision.AnalysisId).OrderBy(id => id, StringComparer.Ordinal).ToArray()
            : [];
        var negativeEvidence = BuildNegativeEvidence(matchedDisapprovedIds, seen.Count, validated.CanClassifyMorphology);
        var conflicts = CombineConflicts(_decisionConflicts, observedConflicts,
            BuildSpellingConflict(_decisions, _spelling));
        var spellingAcceptance = SpellingAcceptance(_spelling, seen.Count, validated);
        var approvedState = ApprovalState(validated, seen.Count, matchedApprovedMorphologies.Count,
            _approvedMorphologyKeys.Count, conflicts.Length > 0);

        return new HumanAgreementFacts(
            validated.Availability,
            validated.Completion,
            Freeze(validated.Limitations),
            approvedState,
            seen.Count,
            _approvedMorphologyKeys.Count,
            matchedApprovedMorphologies.Count,
            Freeze(unmatchedExpectedIds),
            Freeze(provenMissingIds),
            Freeze(matchedApprovedIds),
            Freeze(matchedDisapprovedIds),
            Freeze(matchedUnknownIds),
            Freeze(unmatchedProduced),
            Freeze(conflicts.Select(conflict => conflict with { AnalysisIds = Freeze(conflict.AnalysisIds) })),
            spellingAcceptance,
            Freeze(negativeEvidence));
    }

    private HumanAgreementFacts CreateUnavailableFacts(ValidatedEvidence validated)
    {
        var conflicts = _decisionConflicts.Concat(BuildSpellingConflict(_decisions, _spelling)).ToArray();
        return new HumanAgreementFacts(
            validated.Availability,
            validated.Completion,
            Freeze(validated.Limitations),
            ApprovalState(validated, 0, 0, _approvedMorphologyKeys.Count, conflicts.Length > 0),
            0,
            _approvedMorphologyKeys.Count,
            0,
            [],
            [],
            [],
            [],
            [],
            [],
            Freeze(conflicts.Select(conflict => conflict with { AnalysisIds = Freeze(conflict.AnalysisIds) })),
            SpellingAcceptance(_spelling, 0, validated),
            []);
    }

    private static HumanAgreementApprovalState ApprovalState(
        ValidatedEvidence evidence,
        int producedCount,
        int matchedApprovedCount,
        int expectedApprovedCount,
        bool hasConflict)
    {
        if (evidence.Completion == HumanAgreementSearchCompletion.NotMeasured)
            return HumanAgreementApprovalState.NotMeasured;
        if (evidence.Availability == HumanAgreementEvidenceAvailability.Unavailable ||
            evidence.Completion == HumanAgreementSearchCompletion.Unavailable ||
            evidence.Limitations.Contains(HumanAgreementLimitation.InvalidExpectedMorphology) ||
            evidence.Limitations.Contains(HumanAgreementLimitation.ProjectionUnavailable))
            return HumanAgreementApprovalState.Unavailable;
        if (hasConflict) return HumanAgreementApprovalState.ConflictingDecisions;
        if (expectedApprovedCount == 0) return HumanAgreementApprovalState.NoApprovedExpectations;
        if (evidence.Availability != HumanAgreementEvidenceAvailability.Available ||
            evidence.Completion == HumanAgreementSearchCompletion.Incomplete)
            return HumanAgreementApprovalState.Incomplete;
        if (producedCount == 0) return HumanAgreementApprovalState.NoParse;
        if (matchedApprovedCount == 0) return HumanAgreementApprovalState.NoApprovedMatch;
        return producedCount == 1
            ? HumanAgreementApprovalState.ApprovedUnique
            : HumanAgreementApprovalState.ApprovedWithAlternatives;
    }

    private static HumanAgreementSpellingAcceptance SpellingAcceptance(
        CapturedSpellingStatus spelling,
        int producedCount,
        ValidatedEvidence evidence)
    {
        if (spelling == CapturedSpellingStatus.Unavailable) return HumanAgreementSpellingAcceptance.Unavailable;
        if (spelling != CapturedSpellingStatus.Incorrect) return HumanAgreementSpellingAcceptance.NotApplicable;
        if (producedCount > 0) return HumanAgreementSpellingAcceptance.AcceptedMisspelling;
        if (evidence.Completion == HumanAgreementSearchCompletion.NotMeasured ||
            evidence.Availability == HumanAgreementEvidenceAvailability.Unavailable ||
            evidence.Completion == HumanAgreementSearchCompletion.Unavailable)
            return HumanAgreementSpellingAcceptance.Unavailable;
        if (evidence.Completion == HumanAgreementSearchCompletion.Incomplete)
            return HumanAgreementSpellingAcceptance.Incomplete;
        if (!evidence.CanProveSearchEmpty) return HumanAgreementSpellingAcceptance.Unavailable;
        return HumanAgreementSpellingAcceptance.RejectedMisspelling;
    }

    private HumanAgreementNegativeEvidence[] BuildNegativeEvidence(
        IReadOnlyList<string> matchedDisapprovedIds,
        int producedCount,
        bool canClassifyMorphology)
    {
        var evidence = new List<HumanAgreementNegativeEvidence>();
        if (canClassifyMorphology && matchedDisapprovedIds.Count > 0)
            evidence.Add(new HumanAgreementNegativeEvidence(HumanAgreementNegativeEvidenceKind.DisapprovedParse,
                HumanAgreementClassifier.Freeze(matchedDisapprovedIds)));
        if (canClassifyMorphology && _spelling == CapturedSpellingStatus.Incorrect && producedCount > 0)
            evidence.Add(new HumanAgreementNegativeEvidence(HumanAgreementNegativeEvidenceKind.MisspelledParse, []));
        return evidence.ToArray();
    }

    private static string[] IdsFor(HashSet<DecisionEntry> decisions, HumanAgreementOpinion opinion) =>
        decisions.Where(decision => decision.Opinion == opinion).Select(decision => decision.AnalysisId)
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();

    private static HumanAgreementProducedIdentity ProducedIdentity(ParseAnalysis analysis) =>
        new(Array.AsReadOnly(analysis.Morphs.Select(morph => new HumanAgreementProducedMorph(
            morph.Form!, morph.Msa!, morph.InflType,
            morph.GuessedString is null ? null : HumanAgreementClassifier.Nfd(morph.GuessedString))).ToArray()));

    private static HumanAgreementConflict[] BuildDecisionConflicts(
        IReadOnlyDictionary<string, DecisionEntry[]> byMorphologyKey) => byMorphologyKey.Values
        .Where(entries => entries.Any(entry => entry.Opinion == HumanAgreementOpinion.Approved) &&
            entries.Any(entry => entry.Opinion == HumanAgreementOpinion.Disapproved))
        .Select(entries => new HumanAgreementConflict(HumanAgreementConflictKind.OpposedOpinions,
            entries.Where(entry => entry.Opinion is HumanAgreementOpinion.Approved or HumanAgreementOpinion.Disapproved)
                .Select(entry => entry.AnalysisId).OrderBy(id => id, StringComparer.Ordinal).ToArray()))
        .OrderBy(conflict => conflict.AnalysisIds[0], StringComparer.Ordinal).ToArray();

    private static HumanAgreementConflict? ObservedDecisionConflict(IReadOnlySet<DecisionEntry> decisions)
    {
        if (!decisions.Any(entry => entry.Opinion == HumanAgreementOpinion.Approved) ||
            !decisions.Any(entry => entry.Opinion == HumanAgreementOpinion.Disapproved)) return null;

        return new HumanAgreementConflict(HumanAgreementConflictKind.OpposedOpinions,
            decisions.Where(entry => entry.Opinion is HumanAgreementOpinion.Approved or HumanAgreementOpinion.Disapproved)
                .Select(entry => entry.AnalysisId).OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    private static HumanAgreementConflict[] CombineConflicts(params IEnumerable<HumanAgreementConflict>[] groups)
    {
        var conflicts = new List<HumanAgreementConflict>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var conflict in groups.SelectMany(group => group))
            if (seen.Add(ConflictKey(conflict))) conflicts.Add(conflict);
        return conflicts.ToArray();
    }

    private static string ConflictKey(HumanAgreementConflict conflict)
    {
        var builder = new StringBuilder();
        builder.Append(((int)conflict.Kind).ToString(CultureInfo.InvariantCulture)).Append(';');
        foreach (var id in conflict.AnalysisIds)
            builder.Append(id.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(id).Append(';');
        return builder.ToString();
    }

    private static IEnumerable<HumanAgreementConflict> BuildSpellingConflict(
        IReadOnlyList<DecisionEntry> decisions,
        CapturedSpellingStatus spelling)
    {
        if (spelling != CapturedSpellingStatus.Incorrect) return [];
        var approved = decisions.Where(decision => decision.Opinion == HumanAgreementOpinion.Approved)
            .Select(decision => decision.AnalysisId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        return approved.Length == 0
            ? []
            : [new HumanAgreementConflict(HumanAgreementConflictKind.ApprovedWithIncorrectSpelling, approved)];
    }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
}
