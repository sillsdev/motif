using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Corpus.Ledger;

/// <summary>
/// Turns saved baseline word Assessments into the per-type checks the Coverage report consumes. For each word type
/// the latest finished check (a proved miss or a proved parse) at the report's grammar pin wins; failing that, the
/// latest unfinished check at the pin; failing that, the latest check at another pin, marked not comparable.
/// </summary>
internal static class TypeCheckProducer
{
    private static readonly string[] Kinds = ["ParseTime", "Correctness"];

    /// <summary>Reads every saved baseline word Assessment of the usual kinds through the existing repository seam.</summary>
    public static IReadOnlyDictionary<string, TypeCheck> FromStore(IAssessmentRepository repository, string grammarPin) =>
        Produce(Kinds.SelectMany(repository.ListBaselineAssessments), grammarPin);

    public static IReadOnlyDictionary<string, TypeCheck> Produce(IEnumerable<AssessmentRecord> assessments, string grammarPin)
    {
        var ordered = assessments.Where(item => item.Subject is AssessmentSubject.Words && item.Words is not null)
            .OrderBy(item => item.SavedUtc, StringComparer.Ordinal).ToArray();
        var best = new Dictionary<string, (int Rank, TypeCheck Check)>(StringComparer.Ordinal);
        foreach (var assessment in ordered)
        {
            var atPin = LedgerPins.Same(assessment.GrammarSourceSha256, grammarPin);
            var limits = Limits.Of(assessment);
            foreach (var word in assessment.Words!)
            {
                if (word.LowercaseOf is not null) continue;
                var check = Classify(word, limits, assessment.GrammarSourceSha256, atPin);
                // A finished check is a proved miss or a proved parse; an unfinished one never overrides it.
                var finished = check.Completeness == CheckCompleteness.Complete || check.ProvedPresent;
                var rank = (atPin ? 2 : 0) + (finished ? 1 : 0);
                var key = LedgerJoiner.Key(word.Word);
                // Later assessments replace earlier ones of the same rank or better, so the latest wins within a rank.
                if (!best.TryGetValue(key, out var held) || rank >= held.Rank) best[key] = (rank, check);
            }
        }
        return best.ToDictionary(item => item.Key, item => item.Value.Check, StringComparer.Ordinal);
    }

    private static TypeCheck Classify(AssessedWord word, Limits limits, string assessmentPin, bool atPin)
    {
        var comparable = atPin;
        var incomparable = atPin ? CheckIncomparable.None : CheckIncomparable.RevisionIncompatible;
        TypeCheck Make(CheckCompleteness completeness, bool present, IReadOnlyCollection<string>? produced) =>
            new(completeness, comparable, present, false, produced, assessmentPin, incomparable);

        if (!word.Outcome.TryParseStoredOutcome(out var outcome)) return Make(CheckCompleteness.Partial, false, null);
        var morphology = word.Morphology;
        var stopped = outcome == WordOutcome.TimedOut || morphology?.TimedOut == true;
        var capped = outcome is WordOutcome.Capped or WordOutcome.MemoryCapped || morphology?.Capped == true;
        var invalid = morphology?.InvalidShape == true;
        var unavailable = morphology is not null && (morphology.Unavailable.Count > 0 || morphology.Refusal is not null);
        var nativePaths = morphology?.Analyses.Where(analysis =>
            analysis.Morphs.Count != 0 && analysis.Morphs.All(morph => morph.GuessedString is null)).ToArray();
        var foundPath = nativePaths is null ? word.Analyses.Count > 0 : nativePaths.Length > 0;
        var success = outcome != WordOutcome.Skipped && !invalid && foundPath;
        var key = LedgerJoiner.Key(word.Word);
        var limited = limits.UnsupportedGrammar || limits.RetryLimited.Contains(key);
        var finished = !word.IsIncomplete && !stopped && !capped && !invalid && !unavailable && !limited;
        // A found path proves presence even from a stopped search, but without R1a identity it names no Reading set.
        if (success) return Make(CheckCompleteness.Partial, true, null);
        var miss = finished && outcome == WordOutcome.NoAnalysis && (morphology?.Analyses.Count ?? 0) == 0;
        return miss ? Make(CheckCompleteness.Complete, false, Array.Empty<string>()) : Make(CheckCompleteness.Partial, false, null);
    }

    private sealed record Limits(bool UnsupportedGrammar, HashSet<string> RetryLimited)
    {
        public static Limits Of(AssessmentRecord assessment)
        {
            var invocation = assessment.Invocation;
            var retried = new HashSet<string>(StringComparer.Ordinal);
            if (invocation is null) return new(false, retried);
            if (invocation.StoppedWordRetry is { Failure: null } retry &&
                (retry.RetryStandardErrorSha256 is null || retry.GrammarWarningLines.Any(SIL.Motif.Host.PanGloss.BatchInvocationEvidence.IsUnsupportedGrammarWarning)))
                retried.UnionWith(retry.ReplacedWords().Select(LedgerJoiner.Key));
            return new(invocation.FirstPassGrammarWarningLines.Any(SIL.Motif.Host.PanGloss.BatchInvocationEvidence.IsUnsupportedGrammarWarning), retried);
        }
    }
}
