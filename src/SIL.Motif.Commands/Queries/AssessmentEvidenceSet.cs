using System.Globalization;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>One selected measurement and its explicit replacements, projected once with each word's origin.</summary>
public sealed class AssessmentEvidenceSet
{
    private AssessmentEvidenceSet(AssessmentRecord assessment, IReadOnlyList<AssessmentRecord> replacements)
    {
        Components = Array.AsReadOnly(new[] { assessment }.Concat(replacements).ToArray());
        Words = Array.AsReadOnly(AssessmentWordOverlay.Apply(WordsOf(assessment), replacements.Select(run =>
            run with { Details = ((AssessmentRecordDetails.Words)run.Details) with { Rows = WordsOf(run) } }).ToArray()).ToArray());
        ObjectTimings = Array.AsReadOnly(AssessmentWordOverlay.ApplyObjectTimings(assessment, replacements).ToArray());
        var limited = Components.Select(component => component.TimingEvidence)
            .Where(evidence => evidence is { DetailComplete: false }).Select(evidence => evidence!).ToArray();
        TimingDetail = limited.Length == 0 ? new ObjectTimingDetail(true) : new ObjectTimingDetail(false,
            string.Join(" ", limited.Select(evidence =>
                evidence.StatisticsUnavailable ? "Object timing was not collected." :
                $"Object timing detail kept for the {evidence.DetailWordCount} costliest words" +
                (evidence.HasRetriedWords ? " from the initial pass; some words were retried." : "."))))
        { DetailWordCounts = limited.Where(evidence => !evidence.StatisticsUnavailable)
                .Select(evidence => evidence.DetailWordCount).ToArray(),
            HasRetriedWords = limited.Any(evidence => evidence.HasRetriedWords),
            StatisticsUnavailable = limited.Any(evidence => evidence.StatisticsUnavailable) };
    }

    /// <summary>The complete run followed by the explicit replacements contributing answers.</summary>
    public IReadOnlyList<AssessmentRecord> Components { get; }
    /// <summary>The effective words, retaining their producing Assessment and measurement time.</summary>
    public IReadOnlyList<AssessedWord> Words { get; }
    /// <summary>The object times from the same producing runs as <see cref="Words"/>.</summary>
    public IReadOnlyList<AssessmentObjectTiming> ObjectTimings { get; }

    /// <summary>Whether per-word object timing detail covers the contributing runs.</summary>
    public ObjectTimingDetail TimingDetail { get; }

    /// <summary>Creates a value snapshot after replacement policy has selected its component runs.</summary>
    public static AssessmentEvidenceSet Create(AssessmentRecord assessment, IReadOnlyList<AssessmentRecord> replacements) =>
        new(assessment, replacements);

    /// <summary>Attaches a stored run's provenance without changing its recorded words.</summary>
    public static IReadOnlyList<AssessedWord> WordsOf(AssessmentRecord assessment)
    {
        var origin = new WordMeasurementOrigin(assessment.AssessmentId, assessment.Invocation?.InvocationId,
            DateTimeOffset.Parse(assessment.SavedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        return (assessment.Words ?? []).Select(word => word with { Origin = origin }).ToArray();
    }
}
