using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Host.Assess;

/// <summary>Converts a stored per-word outcome string back to the enum it was written from.</summary>
internal static class StoredWordOutcome
{
    /// <exception cref="ReportRefusalException"><paramref name="outcome"/> is not a recognised stored value.</exception>
    public static WordOutcome Parse(string outcome, string reportKind) =>
        outcome.TryParseStoredOutcome(out var parsed)
            ? parsed
            : throw new ReportRefusalException(reportKind, $"stored word outcome '{outcome}' is not recognised.");
}

/// <summary>
/// Grammar coverage — the share of a scope's words the parser analysed — rendered from a <c>ParseTime</c>
/// Assessment's own stored words and outcomes. Delegates nothing to an Assessor and reimplements no part of
/// <see cref="GrammarCoverageFigure"/>; this type only rebuilds the <see cref="BatchAnalysis"/> that figure
/// already knows how to read.
/// </summary>
public sealed class CoverageReportProducer : IReportProducer
{
    /// <summary>The registry name this kind is asked for under.</summary>
    public const string KindName = "coverage";

    /// <inheritdoc />
    public string Kind => KindName;

    /// <inheritdoc />
    public string Description => "Grammar coverage: the share of a scope's words the parser analysed.";

    /// <inheritdoc />
    public RenderedReport Produce(ReportableAssessment assessment, ReportQuery query, IAssessorCatalog assessors)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (!assessment.Kind.IsStoredKind(AssessmentKind.ParseTime))
        {
            throw new ReportRefusalException(KindName,
                $"this Assessment is a '{assessment.Kind}' measurement; a coverage report needs one collected " +
                "as 'ParseTime' (per-word outcome under a timing pass), which this scope did not collect.");
        }

        var scope = ScopeCodec.ReadTrial(assessment.ScopeJson, KindName);
        var words = assessment.Words.Select((word, index) => new WordAnalysis(
            index, word.Word, 0, StoredWordOutcome.Parse(word.Outcome, KindName), string.Empty)).ToList();
        var batch = new BatchAnalysis(
            words, (int)scope.PerWordLimit.TotalMilliseconds, string.Empty, Array.Empty<string>()) { PerWordStepLimit = scope.PerWordStepLimit };
        var selection = new Selection(assessment.SelectionName, assessment.SelectionWords, assessment.SelectionSha256);
        var figure = GrammarCoverageFigure.Compute(batch, selection, assessment.GrammarSourceSha256);
        return new RenderedReport(KindName, figure.Describe(assessment.SelectionSha256, assessment.GrammarSourceSha256));
    }
}

/// <summary>Builds a coverage denominator only from completed, comparable approved expectations.</summary>
internal static class CorrectnessCoverage
{
    public static GrammarCoverageFigure Compute(
        IReadOnlyList<AssessedWord> words, StoredScope.Trial scope, Selection selection,
        string grammarSourceSha256, string reportKind)
    {
        var compared = words.Select((word, index) => new WordAnalysis(index, word.Word, 0,
            Require(word, reportKind).Status switch
            {
                "covered" => WordOutcome.Analysed,
                "unmatched" => WordOutcome.NoAnalysis,
                "incomplete" => WordOutcome.Capped,
                _ => WordOutcome.Skipped,
            }, string.Empty)).ToArray();
        return GrammarCoverageFigure.Compute(new BatchAnalysis(compared,
            (int)scope.PerWordLimit.TotalMilliseconds, string.Empty, [])
            { PerWordStepLimit = scope.PerWordStepLimit }, selection, grammarSourceSha256);
    }

    internal static SIL.Motif.Contract.Responses.WordCorrectness Require(AssessedWord word, string reportKind)
    {
        if (word.Correctness is null || word.Morphology is null)
            throw new ReportRefusalException(reportKind, "The word has no approved morphology comparison evidence.");
        return MorphologyCorrectness.Compare(word.Morphology, word.Correctness.Expectations);
    }
}

/// <summary>Reports every approved reading's match separately from search completion.</summary>
public sealed class CorrectnessReportProducer : IReportProducer
{
    public const string KindName = "correctness";
    public string Kind => KindName;
    public string Description => "Approved morphology matches, with incomplete and unavailable searches explicit.";

    public RenderedReport Produce(ReportableAssessment assessment, ReportQuery query, IAssessorCatalog assessors)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (!assessment.Kind.IsStoredKind(AssessmentKind.Correctness))
            throw new ReportRefusalException(KindName,
                $"This Assessment is '{assessment.Kind}'; a correctness report requires 'Correctness'.");
        var rows = assessment.Words.Select(word => (word, result: CorrectnessCoverage.Require(word, KindName))).ToArray();
        var complete = rows.Count(row => !row.word.Morphology!.Capped && !row.word.Morphology.TimedOut && !row.word.Morphology.InvalidShape);
        var incomplete = rows.Count(row => row.word.Morphology!.Capped || row.word.Morphology.TimedOut);
        var text = new System.Text.StringBuilder();
        text.AppendLine($"{complete} searches completed; {incomplete} incomplete.");
        text.AppendLine($"{rows.Sum(row => row.result.Matched)}/{rows.Sum(row => row.result.Expected)} approved readings matched.");
        text.AppendLine($"{rows.Count(row => row.result.Status == "covered")} words covered; " +
            $"{rows.Count(row => row.result.Status == "unmatched")} unmatched; " +
            $"{rows.Count(row => row.result.Unavailable.Count > 0 || row.word.Morphology!.InvalidShape)} unavailable; " +
            $"{rows.Count(row => row.result.Expected == 0)} without approved expectations.");
        foreach (var (word, result) in rows)
        {
            var limits = string.Join(" and ", new[]
                { word.Morphology!.Capped ? "step limit" : null, word.Morphology.TimedOut ? "time limit" : null }
                .OfType<string>());
            var completion = word.Morphology!.Capped || word.Morphology.TimedOut
                ? $"INCOMPLETE — parsing did not finish ({limits})"
                : word.Morphology.InvalidShape ? "Not attempted" : "Search completed";
            text.AppendLine($"{word.Word}: {completion}; {result.Matched}/{result.Expected} approved readings matched; {result.Status}");
            foreach (var reason in result.Unavailable) text.AppendLine($"  Unavailable: {reason}");
        }
        return new RenderedReport(KindName, text.ToString());
    }
}

/// <summary>
/// A comparison between two Assessments, rendered from a <c>Difference</c> Assessment's own stored rows —
/// what <c>compare</c> produced and stored, never recomputed from the two inputs it was made from. Each
/// stored word is one <see cref="WordChange"/> that survived the join; a word that behaved identically on
/// both sides was never written and so never appears here either.
/// </summary>
public sealed class DifferenceReportProducer : IReportProducer
{
    /// <summary>The registry name this kind is asked for under.</summary>
    public const string KindName = "difference";

    /// <inheritdoc />
    public string Kind => KindName;

    /// <inheritdoc />
    public string Description => "A comparison between two Assessments, joined on the word.";

    /// <inheritdoc />
    public RenderedReport Produce(ReportableAssessment assessment, ReportQuery query, IAssessorCatalog assessors)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (!assessment.Kind.IsStoredKind(AssessmentKind.Difference))
        {
            throw new ReportRefusalException(KindName,
                $"this Assessment is a '{assessment.Kind}' measurement; a difference report needs one " +
                "collected as 'Difference' (a comparison between two Assessments), which this scope did " +
                "not collect.");
        }

        var meta = ScopeCodec.ReadDifference(assessment.ScopeJson, KindName);
        var text = new System.Text.StringBuilder();
        text.AppendLine($"Comparing {meta.FromAssessmentId} -> {meta.ToAssessmentId}");
        text.AppendLine(
            $"  Words: {meta.FromWordCount} vs {meta.ToWordCount}, {meta.SharedWordCount} shared, " +
            $"{assessment.Words.Count} changed");
        if (meta.TokeniserMismatch) text.AppendLine("  WARNING: " + meta.TokeniserWarning);
        foreach (var word in assessment.Words.OrderBy(w => w.Word, StringComparer.Ordinal))
            text.AppendLine($"    {word.Word}: {word.Outcome}");
        return new RenderedReport(KindName, text.ToString());
    }
}
