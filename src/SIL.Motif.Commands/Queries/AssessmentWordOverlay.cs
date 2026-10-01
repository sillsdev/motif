using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>Applies later subset Assessments to the words of one complete Assessment.</summary>
public static class AssessmentWordOverlay
{
    /// <summary>
    /// Selects only explicit reparses of this complete run, on its exact Baseline and within its word set.
    /// Exploratory runs and reads of history carry no replacement intent and cannot change its answers.
    /// </summary>
    public static IReadOnlyList<AssessmentRecord> ReplacementsFor(AssessmentRecord assessment,
        IReadOnlyList<AssessmentRecord> candidates)
    {
        var words = assessment.Selection.Words.ToHashSet(StringComparer.Ordinal);
        return candidates.Where(candidate => candidate.ReplacesAssessmentId == assessment.AssessmentId &&
                candidate.BaselineToken == assessment.BaselineToken && candidate.Kind == assessment.Kind &&
                candidate.ProposalId is null && string.CompareOrdinal(candidate.SavedUtc, assessment.SavedUtc) > 0 &&
                candidate.Selection.Words.Count > 0 && candidate.Selection.Words.All(words.Contains))
            .OrderBy(candidate => candidate.SavedUtc, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.AssessmentId, StringComparer.Ordinal).ToArray();
    }
    /// <summary>Replaces each measured word in recorded order, retaining untouched words.</summary>
    public static IReadOnlyList<AssessedWord> Apply(IReadOnlyList<AssessedWord> baseline,
        IReadOnlyList<AssessmentRecord> reruns)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(reruns);
        var current = baseline.ToDictionary(word => word.Word, StringComparer.Ordinal);
        foreach (var rerun in reruns)
            foreach (var word in rerun.Words ?? [])
                if (current.TryGetValue(word.Word, out var previous))
                    current[word.Word] = word with { OccurrenceCount = word.OccurrenceCount ?? previous.OccurrenceCount };
        return baseline.Select(word => current[word.Word]).ToArray();
    }

    /// <summary>
    /// Replaces each re-run word's object times with the re-run's, so they come from the same run as its word time.
    /// Words the complete Assessment did not measure stay out, as <see cref="Apply"/> leaves them out.
    /// </summary>
    public static IReadOnlyList<AssessmentObjectTiming> ApplyObjectTimings(AssessmentRecord baseline,
        IReadOnlyList<AssessmentRecord> reruns)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(reruns);
        var measured = (baseline.Words ?? []).Select(word => word.Word).ToHashSet(StringComparer.Ordinal);
        var rows = baseline.ObjectTimings.ToList();
        foreach (var rerun in reruns)
        {
            var replaced = (rerun.Words ?? []).Select(word => word.Word).Where(measured.Contains)
                .ToHashSet(StringComparer.Ordinal);
            rows.RemoveAll(row => replaced.Contains(row.Word));
            rows.AddRange(rerun.ObjectTimings.Where(row => replaced.Contains(row.Word)));
        }
        return rows;
    }
}
