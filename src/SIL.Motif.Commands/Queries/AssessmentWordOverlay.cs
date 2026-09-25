using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>Applies later subset Assessments to the words of one complete Assessment.</summary>
public static class AssessmentWordOverlay
{
    /// <summary>Replaces each measured word in recorded order, retaining untouched words.</summary>
    public static IReadOnlyList<AssessedWord> Apply(IReadOnlyList<AssessedWord> baseline,
        IReadOnlyList<AssessmentRecord> reruns)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(reruns);
        var current = baseline.ToDictionary(word => word.Word, StringComparer.Ordinal);
        foreach (var rerun in reruns)
            foreach (var word in rerun.Words ?? [])
                if (current.ContainsKey(word.Word)) current[word.Word] = word;
        return baseline.Select(word => current[word.Word]).ToArray();
    }
}
