using SIL.Motif.Host.Parser;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.Assess;

/// <summary>
/// Whether a Proposal has been measured well enough to apply (CONTEXT.md's Readiness): an Assessment
/// covers its current content, that Assessment measured the project state the apply would land on, and
/// applying it would show no regression. Computed from evidence, never granted by a caller — every caller
/// asks the same question, whether that is <c>apply</c> deciding to refuse or a future reader just showing
/// the answer.
/// </summary>
public static class Readiness
{
    /// <summary>Readiness reasons and the separate regression refusal signal.</summary>
    public sealed record Decision(
        IReadOnlyList<string> Reasons,
        bool IsRegression,
        bool CorrectnessAssessmentRequired = true,
        string? CorrectnessAssessmentExemptionReason = null,
        bool HasDroppedGrammar = false,
        string? DroppedGrammarReason = null,
        DroppedGrammar? Dropped = null);

    /// <summary>
    /// The reasons a Proposal is not ready to apply, in the same wording a refusal message quotes verbatim.
    /// Empty means ready. <paramref name="candidate"/> is the Assessment covering the content that would be
    /// applied, or <c>null</c> if none exists. <paramref name="current"/> is the project's current
    /// Assessment, already narrowed to <c>null</c> by the caller unless it is of
    /// <see cref="RegressionChecker.RequiredKind"/> — only that kind is comparable for a regression.
    /// <paramref name="currentBaselineToken"/> is the current Assessment's Baseline token regardless of its
    /// kind (or <c>null</c> if there is no current Assessment at all), since staleness is judged on any
    /// current Assessment, not only a <c>Correctness</c> one.
    /// </summary>
    public static IReadOnlyList<string> Assess(
        CorrectnessAssessment? candidate,
        CorrectnessAssessment? current,
        string? currentBaselineToken,
        string candidateBaselineToken,
        bool gateOnRegression,
        IReadOnlyCollection<string>? changedWords = null,
        Func<string, string?>? lowercaseOf = null) =>
        Evaluate(candidate, current, currentBaselineToken, candidateBaselineToken, gateOnRegression,
            changedWords, lowercaseOf: lowercaseOf).Reasons;

    /// <summary>Evaluates Readiness while retaining whether a regression caused the refusal.</summary>
    public static Decision Evaluate(
        CorrectnessAssessment? candidate,
        CorrectnessAssessment? current,
        string? currentBaselineToken,
        string candidateBaselineToken,
        bool gateOnRegression,
        IReadOnlyCollection<string>? changedWords = null,
        string? correctnessAssessmentExemptionReason = null,
        DroppedGrammar? droppedGrammar = null,
        Func<string, string?>? lowercaseOf = null)
    {
        if (!string.IsNullOrWhiteSpace(correctnessAssessmentExemptionReason))
            return new Decision([], false, false, correctnessAssessmentExemptionReason);

        if (candidate is null)
            return new Decision(["no Assessment covers its current content, so nothing has measured what it would do"], false);

        var reasons = new List<string>();
        if (changedWords is { Count: > 0 })
        {
            var selected = candidate.Selection.Words.Select(word =>
                word.Normalize(System.Text.NormalizationForm.FormD)).ToHashSet(StringComparer.Ordinal);
            var measured = candidate.Words.Select(word =>
                word.Word.Normalize(System.Text.NormalizationForm.FormD)).ToHashSet(StringComparer.Ordinal);
            var missing = changedWords.Select(word => word.Normalize(System.Text.NormalizationForm.FormD))
                .Where(word => !selected.Contains(word) || !measured.Contains(word))
                .Distinct(StringComparer.Ordinal).OrderBy(word => word, StringComparer.Ordinal).ToArray();
            if (missing.Length > 0)
                reasons.Add($"its Assessment did not measure changed word(s): {string.Join(", ", missing)}");
        }
        var lacking = candidate.Words.Where(Lacks).ToArray();
        // A capitalised word the parser cannot read as written is covered by its complete lowercase row (ADR 0078).
        lacking = lacking.Where(word => !(word.Morphology is { InvalidShape: true } && lowercaseOf?.Invoke(word.Word) is { } lower &&
            candidate.Words.Any(other => string.Equals(other.Word, lower, StringComparison.Ordinal) && !Lacks(other)))).ToArray();
        // A derived twin of invalid shape is left out; a twin the person typed is in the Selection and counts.
        if (lowercaseOf is not null && lacking.Any(word => word.Morphology is { InvalidShape: true }))
        {
            var typed = TypedWords(candidate.Selection.Words);
            var derived = typed.Select(word => lowercaseOf(word)).OfType<string>()
                .Where(lower => !typed.Contains(lower)).ToHashSet(StringComparer.Ordinal);
            lacking = lacking.Where(word => !(word.Morphology is { InvalidShape: true } &&
                derived.Contains(Nfd(word.Word)))).ToArray();
        }
        var unavailable = lacking.Length > 0;
        foreach (var cause in lacking.GroupBy(LackCause))
        {
            var names = cause.Select(word => word.Word).Distinct(StringComparer.Ordinal).ToArray();
            var shown = string.Join(", ", names.Take(5).Select(word => $"\"{word}\""));
            var more = names.Length > 5 ? $" and {names.Length - 5} more" : "";
            reasons.Add($"its per-word correctness evidence is incomplete or unavailable for {shown}{more}: {cause.Key}");
        }

        // Two Assessments only mean anything against each other when they measured the same project state.
        if (currentBaselineToken is not null &&
            !string.Equals(currentBaselineToken, candidateBaselineToken, StringComparison.Ordinal))
        {
            reasons.Add(
                "its Assessment was measured against a different project state than the current one, so it has " +
                "not been re-run since the project moved");
        }

        var isRegression = false;
        if (!unavailable && gateOnRegression && current is not null)
        {
            try
            {
                var finding = RegressionChecker.Check(current, candidate);
                if (finding is { IsRegression: true })
                {
                    reasons.Add($"it would be a regression: {finding.Describe()}");
                    isRegression = true;
                }
            }
            catch (AmbiguousWordCasesException exception)
            {
                // A check that could not run is a reason it is not ready, never a silent pass.
                reasons.Add($"its regression check could not run: {exception.Message}");
            }
        }

        var hasDroppedGrammar = droppedGrammar is { Introduced.Count: > 0 };
        string? droppedReason = null;
        if (hasDroppedGrammar) droppedReason = DescribeDroppedGrammar(droppedGrammar!);
        // Missing evidence, named once the Trial is otherwise ready so it is not buried among other reasons.
        else if (droppedGrammar is { Checked: false } && reasons.Count == 0)
            droppedReason = $"dropped grammar was not checked ({droppedGrammar.NotCheckedReason?.TrimEnd('.')}); " +
                "try the Proposal again";
        if (droppedReason is not null) reasons.Add(droppedReason);
        return new Decision(reasons, isRegression, HasDroppedGrammar: hasDroppedGrammar,
            DroppedGrammarReason: droppedReason, Dropped: hasDroppedGrammar ? droppedGrammar : null);
    }

    internal static string Nfd(string word) => word.Normalize(System.Text.NormalizationForm.FormD);

    /// <summary>The words the person asked about, spelled as the Assessment stores them.</summary>
    internal static HashSet<string> TypedWords(IEnumerable<string> selection) =>
        selection.Select(Nfd).ToHashSet(StringComparer.Ordinal);

    /// <summary>Whether an invalid-shape row is one the person never asked about, which no Readiness rule counts.</summary>
    internal static bool IsUnaskedInvalidShape(AssessedWord word, HashSet<string> typed) =>
        word.Morphology is { InvalidShape: true } && !typed.Contains(Nfd(word.Word));

    // Names why one word lacks complete evidence, so the fix matches the cause.
    private static bool Lacks(AssessedWord word) => word.Correctness is null || word.Morphology is null ||
        CorrectnessCoverage.Require(word, "readiness").Status is "incomplete" or "unavailable";

    private static string LackCause(AssessedWord word) =>
        word.Correctness is null || word.Morphology is null
            ? "the Assessment holds no approved-morphology evidence for them; measure them again"
            : word.Morphology.InvalidShape
                ? "not a single parseable word (a line with spaces is the usual cause); put one word per line with no spaces in the Trial's word list"
                : word.Outcome == "memory-capped"
                    ? "the parser reached its memory limit for the word before finishing; review the word and grammar, then rerun the Trial"
                : word.Morphology.Capped || word.Morphology.TimedOut
                    ? "the parser stopped at its work or time limit before finishing; raise the limit or rerun the Trial when the machine is quieter"
                    : "an approved reading lacks a lexicon Form or MSA identity, or the parser reported its result unavailable; check the approved analysis";

    // Names each dropped object; the way forward is to fix it, in the Proposal or in FieldWorks, and try again.
    private static string DescribeDroppedGrammar(DroppedGrammar dropped) =>
        "its Trial found grammar the parser dropped with only a warning, in what the Proposal adds or edits: " +
        string.Join("; ", dropped.Introduced.Select(finding => finding.Code + " on " +
            (finding.Objects.Count == 0
                ? "an object PanGloss did not identify"
                : string.Join(", ", finding.Objects.Select(item =>
                    $"{item.Kind ?? "object"} {(item.Name is null ? "" : item.Name + " ")}({item.MotifId})"))))) +
        (dropped.OmittedFindings > 0 ? $"; and {dropped.OmittedFindings} more" : "") +
        "; fix what it names, in the Proposal or in FieldWorks, then try the Proposal again";
}
