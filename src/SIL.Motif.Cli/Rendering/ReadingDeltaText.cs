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
            if (delta.Groups is not null) continue;
            foreach (var row in group.Rows)
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {row.Word} ({row.CaseKey}): {row.BeforeCount} before, {row.AfterCount} after, " +
                    $"{row.Retired.Sum(item => item.Count)} retired, {row.Added.Sum(item => item.Count)} added"));
            var rest = group.Total - group.Offset - group.Rows.Count;
            if (rest > 0) text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {rest} more; use --offset or --page."));
        }
        if (delta.Groups is { } groups)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"By {groups.By}: {groups.Total} groups, most tokens first"));
            foreach (var group in groups.Rows)
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {group.Key}{(group.Label is null ? "" : " " + group.Label)}: {group.Retired} retired, {group.Added} added, " +
                    $"{group.Words} words, {group.Tokens} tokens; e.g. {string.Join(", ", group.SampleWords)}"));
            var more = groups.Total - groups.Offset - groups.Rows.Count;
            if (more > 0) text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {more} more groups; use --offset or --page."));
        }
        foreach (var manifest in delta.Manifests)
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"Wrote {manifest.Path}: {manifest.Rows} rows, {manifest.Readings} Readings."));
        if (delta.Expectations is { } gate)
        {
            text.AppendLine("Expectations: " + gate.State + (gate.Reason is null ? "" : " - " + gate.Reason));
            foreach (var row in gate.Mismatches)
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {row.Kind} {row.Word}: {row.Unexpected.Sum(item => item.Count)} unexpected, {row.Missing.Sum(item => item.Count)} missing"));
            if (gate.MismatchedWords > gate.Mismatches.Count)
                text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {gate.MismatchedWords - gate.Mismatches.Count} more mismatches in the JSON."));
        }
        if (delta.Receipt is { } receipt)
            text.AppendLine($"Wrote receipt {receipt.Path} (digest {receipt.ReceiptDigest}, gate code {receipt.GateCodeDigest}).");
        foreach (var limitation in delta.Limitations) text.AppendLine("Limitation: " + limitation);
        return text.ToString();
    }
}
