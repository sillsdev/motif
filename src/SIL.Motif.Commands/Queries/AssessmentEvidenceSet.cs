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
            run with { Words = WordsOf(run) }).ToArray()).ToArray());
        ObjectTimings = Array.AsReadOnly(AssessmentWordOverlay.ApplyObjectTimings(assessment, replacements).ToArray());
    }

    /// <summary>The complete run followed by the explicit replacements contributing answers.</summary>
    public IReadOnlyList<AssessmentRecord> Components { get; }
    /// <summary>The effective words, retaining their producing Assessment and measurement time.</summary>
    public IReadOnlyList<AssessedWord> Words { get; }
    /// <summary>The object times from the same producing runs as <see cref="Words"/>.</summary>
    public IReadOnlyList<AssessmentObjectTiming> ObjectTimings { get; }

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
