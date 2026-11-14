using System.Globalization;
using System.Text;

namespace SIL.Motif.Contract.Parsimony;

/// <summary>
/// The trend line of the weekly sweep: one tab-separated row per (date, class). The counts are the eligible
/// items each measure already reported, so the sweep reads stored results and adds O(measures) work.
/// </summary>
public static class SweepTrend
{
    /// <summary>The header row of a trend file.</summary>
    public const string Header = "date\tclass\tobjects\tfindings";

    /// <summary>Rows summing eligible and finding items per counted unit, in unit order.</summary>
    /// <param name="date">The sweep date.</param>
    /// <param name="runs">The measure runs of the sweep.</param>
    public static IReadOnlyList<string> Rows(DateOnly date, IEnumerable<ParsimonyMeasureRun> runs) =>
        [.. runs.Where(r => r.EligibleItems is not null)
            .GroupBy(r => r.Unit, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => string.Create(CultureInfo.InvariantCulture,
                $"{date:yyyy-MM-dd}\t{g.Key}\t{g.Max(r => r.EligibleItems)}\t{g.Sum(r => r.FindingItems ?? 0)}"))];

    /// <summary>Appends the rows to the trend file, writing the header first when the file is new.</summary>
    /// <param name="path">The trend file.</param>
    /// <param name="rows">Rows from <see cref="Rows"/>.</param>
    public static void Append(string path, IReadOnlyList<string> rows)
    {
        var sb = new StringBuilder();
        if (!File.Exists(path)) sb.Append(Header).Append('\n');
        foreach (var row in rows) sb.Append(row).Append('\n');
        File.AppendAllText(path, sb.ToString());
    }
}
