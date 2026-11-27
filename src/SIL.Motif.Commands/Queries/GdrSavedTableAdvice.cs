using SIL.Motif.Contract.Gdr;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Gdr;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// The advice <c>guide check</c> gives about saved tables (ADR 0005): a table is out of date when it was saved from another Baseline, or
/// when a live run of its query now gives other rows. It is advice like every Guide finding. It never runs a query: it takes the run
/// the check already gave each block, and never refreshes anything.
/// </summary>
internal static class GdrSavedTableAdvice
{
    /// <summary>Advice for each out-of-date saved table, one finding per section.</summary>
    public static IReadOnlyList<GdrSyncFinding> Findings(IReadOnlyList<GdrSavedTableSource> tables, string? baselineDigest,
        Func<GdrSavedTableSource, GuideEvidenceResult?> liveRun)
    {
        var stale = new List<(string Gdr, string Subject)>();
        foreach (var table in tables)
        {
            var other = !GdrSavedTables.IsBaseline(table.Caption, baselineDigest);
            var live = liveRun(table);
            if (live is not { Status: GuideEvidenceStatus.Ready, Table: { } fresh })
            {
                // With no live run the Baseline is the only evidence; saving moves it, so a run settles first.
                if (other) stale.Add((table.Gdr, $"{table.Query.Id} (saved from Baseline {table.Caption.BaselineId}, not the current one, and no live run to compare)"));
                continue;
            }
            var total = fresh.Rows.Count + fresh.Truncated;
            var rows = fresh.Rows.Take(GdrSavedTables.MaxRows).ToArray();
            var same = total == table.Caption.Rows && fresh.Complete != table.Caption.AtLeast && rows.Length == table.Rows.Count &&
                fresh.Columns.SequenceEqual(table.Columns) && rows.Zip(table.Rows).All(pair => Cells(pair.First).SequenceEqual(pair.Second));
            if (!same) stale.Add((table.Gdr, $"{table.Query.Id} (its rows differ from a live run{(other ? $"; saved from Baseline {table.Caption.BaselineId}" : "")})"));
        }
        return [.. stale.GroupBy(item => item.Gdr).Select(group => new GdrSyncFinding(GdrSyncCodes.SavedTableOutOfDate,
            $"{group.Key}: {group.Count()} saved table(s) are out of date. Motif shows a live result, so only what FieldWorks shows is behind; " +
            $"run 'guide refresh-tables {group.Key}' to stage fresh tables.",
            group.Key, null, null, group.Count(), group.Select(item => item.Subject).Take(10).ToArray()))];
    }

    private static IEnumerable<string> Cells(IReadOnlyList<string> row) =>
        row.Select(cell => cell.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Replace('\u2028', ' ').Trim());
}
