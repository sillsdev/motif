using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>Pure aggregations over stored Assessment timing facts.</summary>
public static class TimingAggregation
{
    /// <summary>Builds median, nearest-rank 95th percentile, and the requested slowest words.</summary>
    public static OverviewTiming SummarizeWords(IEnumerable<AssessedWord> words, int top = 3)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (top < 0) throw new ArgumentOutOfRangeException(nameof(top));
        var materialized = words.ToArray();
        var timed = materialized.Where(word => word.ElapsedMs is not null).ToArray();
        var durations = timed.Select(word => word.ElapsedMs!.Value).Order().ToArray();
        var median = durations.Length switch
        {
            0 => (double?)null,
            var length when length % 2 == 1 => durations[length / 2],
            var length => (durations[length / 2 - 1] + durations[length / 2]) / 2d,
        };
        double? percentile95 = durations.Length == 0
            ? null
            : durations[Math.Max(0, (int)Math.Ceiling(durations.Length * 0.95) - 1)];
        var slowest = timed.OrderByDescending(word => word.ElapsedMs).ThenBy(word => word.Word, StringComparer.Ordinal)
            .Take(top).Select(word => new SlowWordTiming(word.Word, word.ElapsedMs!.Value)).ToArray();
        var stepLimited = materialized.Count(word => word.Outcome == "capped" || word.Morphology?.Capped == true);
        return new OverviewTiming(median, percentile95, slowest, stepLimited) { MeasuredWordCount = durations.Length };
    }

    /// <summary>Groups object timing rows by kind or object and finds the most costly words for one object.</summary>
    public static (IReadOnlyList<TimingAggregateRow> Aggregates, IReadOnlyList<WordRuleTiming> CostliestWords)
        Aggregate(IReadOnlyList<AssessmentObjectTiming> rows, string by, string? rule, int top)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (by is not ("kind" or "rule")) throw new ArgumentException("Grouping must be 'kind' or 'rule'.", nameof(by));
        if (top <= 0) throw new ArgumentOutOfRangeException(nameof(top));
        Func<AssessmentObjectTiming, string> key = by == "kind" ? row => row.Kind : row => row.Object;
        var timedRows = rows.Where(row => row.ElapsedNs is not null).ToArray();
        var total = timedRows.Sum(row => row.ElapsedMs!.Value);
        var aggregates = rows.GroupBy(key, StringComparer.Ordinal)
            .Select(group => (Name: group.Key, Rows: group.ToArray(),
                TimedRows: group.Where(row => row.ElapsedNs is not null).ToArray()))
            .Where(group => group.TimedRows.Length != 0)
            .Select(group =>
            {
                var elapsed = group.TimedRows.Sum(row => row.ElapsedMs!.Value);
                return new TimingAggregateRow(group.Name, elapsed, total == 0 ? 0 : elapsed / total,
                    group.Rows.Sum(row => row.Attempts ?? 0),
                    group.Rows.Select(row => row.Word).Distinct(StringComparer.Ordinal).Count())
                {
                    Kind = by == "kind" ? group.Name : group.Rows[0].Kind,
                };
            })
            .OrderByDescending(row => row.ElapsedMs).ThenBy(row => row.Name, StringComparer.Ordinal).ToArray();
        var costliest = rule is null
            ? Array.Empty<WordRuleTiming>()
            : rows.Where(row => StringComparer.Ordinal.Equals(row.Object, rule))
                .GroupBy(row => row.Word, StringComparer.Ordinal)
                .Select(group => (Word: group.Key, Rows: group.ToArray()))
                .Where(group => group.Rows.Any(row => row.ElapsedNs is not null))
                .Select(group => new WordRuleTiming(group.Word,
                    group.Rows.Where(row => row.ElapsedNs is not null).Sum(row => row.ElapsedMs!.Value),
                    group.Rows.Sum(row => row.Attempts ?? 0)))
                .OrderByDescending(row => row.ElapsedMs).ThenBy(row => row.Word, StringComparer.Ordinal)
                .Take(top).ToArray();
        return (aggregates, costliest);
    }
}
