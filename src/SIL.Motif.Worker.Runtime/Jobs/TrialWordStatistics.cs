using Microsoft.Data.Sqlite;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Worker.Jobs;

/// <summary>
/// Gives a Trial's recorded words the per-word attempt and work counts an Assess already records, read from the
/// statistics cache the same batch wrote, so a Baseline and a Trial can be compared in steps.
/// </summary>
internal static class TrialWordStatistics
{
    /// <summary>
    /// Selects each word's final statistics, allowing absent caches to leave counters unknown.
    /// </summary>
    internal static IReadOnlyDictionary<string, PanGlossWordStatistics>? Read(
        IEnumerable<ProducedAssessment> produced, IReadOnlyCollection<string> words)
    {
        var measurements = produced.ToArray();
        var cache = measurements.Select(item => item.Raw).OfType<AssessmentRaw.FileCache>().FirstOrDefault();
        IReadOnlyDictionary<string, PanGlossWordStatistics>? statistics = null;
        if (cache is not null)
        {
            try
            {
                statistics = PanGlossBatchStatisticsReader.Read(cache.Path, words, _ => 0,
                    maxObjectTimingRows: 0).Words;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or
                UnauthorizedAccessException or SqliteException or ArgumentException)
            {
                statistics = null;
            }
        }
        return BatchAnalysis.OverlayRetryStatistics(statistics,
            measurements.Select(item => item.Raw).OfType<AssessmentRaw.Batch>().SelectMany(item => item.Analysis.Words));
    }

    /// <summary>Returns <paramref name="word"/> with its counts filled from <paramref name="statistics"/>, when known.</summary>
    internal static AssessedWord Apply(AssessedWord word, IReadOnlyDictionary<string, PanGlossWordStatistics>? statistics)
    {
        if (statistics is null || !statistics.TryGetValue(word.Word, out var found)) return word;
        return word with
        {
            Attempts = found.Attempts,
            Passes = found.Passes,
            WorkSteps = found.WorkSteps ?? word.WorkSteps,
            Morphology = word.Morphology is { } morphology ? morphology with { Attempts = found.Attempts } : null,
        };
    }
}
