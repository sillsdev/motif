using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Review;

namespace SIL.Motif.Cli.Rendering;

internal static class ReadingDeltaText
{
    internal static string Render(ReadingDelta delta)
    {
        var counts = delta.Counts;
        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Reading delta of Trial {delta.JobId}: {counts.Measured} forms measured, {counts.ParsedBefore} parsed before."));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Lost {counts.Lost}, gained {counts.Gained}, changed {counts.Changed}, doubled {counts.Doubled}; " +
            $"{counts.RetiredReadings} Readings retired, {counts.AddedReadings} added; {counts.Unmeasured} unmeasured."));
        if (counts.RejectedNegatives > 0)
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{counts.RejectedNegatives} known non-words stopped parsing."));
        foreach (var group in delta.Classes)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{group.Class}: {group.Total}"));
            foreach (var row in group.Rows)
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {row.Word} ({row.CaseKey}): {row.BeforeCount} before, {row.AfterCount} after, " +
                    $"{row.Retired.Sum(item => item.Count)} retired, {row.Added.Sum(item => item.Count)} added"));
            var rest = group.Total - group.Offset - group.Rows.Count;
            if (rest > 0) text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {rest} more; use --offset or --page."));
        }
        foreach (var manifest in delta.Manifests)
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"Wrote {manifest.Path}: {manifest.Rows} rows, {manifest.Readings} Readings."));
        foreach (var limitation in delta.Limitations) text.AppendLine("Limitation: " + limitation);
        return text.ToString();
    }
}
