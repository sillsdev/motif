using System.Globalization;
using SIL.LCModel;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Commands.Assess;

/// <summary>
/// Decides before the parser starts whether per-object timing would exceed the row budget. The parser keeps one
/// fact per word, object and direction in memory, so the cost is known from the word and object counts alone.
/// </summary>
internal static class ObjectTimingPreflight
{
    // A test narrows the budget for its own async flow without touching any other running test.
    internal static readonly AsyncLocal<int?> RowBudgetForTests = new();

    /// <summary>The one budget for kept rows and for the pre-flight estimate.</summary>
    internal static int RowBudget => RowBudgetForTests.Value ?? PanGlossBatchStatisticsReader.DefaultObjectTimingRowBudget;

    /// <summary>
    /// Words times grammar objects, an upper bound on the rows. A grammar object is an allomorph, the unit the
    /// parser attributes time to, so the count follows the lexicon and not the Selection.
    /// </summary>
    internal static long EstimateRows(LcmCache cache, int words)
    {
        var objects = cache.ServiceLocator.GetInstance<IMoFormRepository>().Count;
        return checked((long)words * Math.Max(1, objects));
    }

    internal static ObjectTimingNote Skipped(long estimatedRows, long budget) => new(ObjectTimingNote.Skipped, budget,
        $"Per-object timing was skipped: about {estimatedRows.ToString("N0", CultureInfo.InvariantCulture)} rows would " +
        $"exceed the limit of {budget.ToString("N0", CultureInfo.InvariantCulture)}; everything else was collected. " +
        "To collect it anyway, list ObjectTiming in `collect` in the project's .motif.toml, or assess fewer words.")
    { EstimatedRows = estimatedRows };

    internal static ObjectTimingNote Truncated(long totalRows, long keptRows, int detailWords, long budget) =>
        new(ObjectTimingNote.Truncated, budget,
            $"Per-object timing detail was cut to the {detailWords.ToString("N0", CultureInfo.InvariantCulture)} costliest " +
            $"words ({keptRows.ToString("N0", CultureInfo.InvariantCulture)} of {totalRows.ToString("N0", CultureInfo.InvariantCulture)} " +
            $"rows) to stay within the limit of {budget.ToString("N0", CultureInfo.InvariantCulture)}; object totals are complete.")
        { TotalRows = totalRows, KeptRows = keptRows, DetailWords = detailWords };
}
