using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli.Rendering;

/// <summary>
/// The text of the timing command and the Overview's speed lines. Parser work, in PanGloss parser work, leads and is
/// what every comparison and share reads, because it does not change with how busy this computer is. A time
/// appears only after it, labelled "time on this computer".
/// </summary>
internal static class TimingTextRenderer
{
    public static string RenderTiming(TimingResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"Parser work for {response.WordCount:N0} word(s) from {response.WordSet} ({response.AssessmentId})");
        text.AppendLine("  " + (response.EvidenceRelation switch
        {
            TimingEvidenceRelation.Current => "Current Baseline",
            TimingEvidenceRelation.Historical => "Historical Baseline",
            TimingEvidenceRelation.SavedSince => "FieldWorks saved since the measured Baseline",
            _ => "Baseline relationship unknown",
        }));
        if (response.ObjectTimingDetail is { IsComplete: false, Description: { } detail }) text.AppendLine("  " + detail);
        if (response.SourceLastWriteUtc is { } capturedSave)
            text.AppendLine($"  Measured FieldWorks save: {capturedSave:O}");
        if (response.Baseline is { } baseline)
            text.AppendLine($"  Baseline captured: {baseline.CapturedUtc}");
        if (response.CurrentProjectIsStale)
            text.AppendLine("  Warning: FieldWorks has changed since the current Baseline.");
        text.AppendLine("  " + WorkLine(response.Work));
        var work = response.ParserWorkAggregates;
        var workTotal = response.Work.TotalParserWork;
        text.AppendLine(response.ObjectTimingAggregatesAvailable
            ? $"  By {response.By}:" : "  Object timing totals unavailable for these words.");
        if (work.Count == 0 && response.Aggregates.Count > 0)
            text.AppendLine($"    Work by {response.By} was not recorded for every one of these words, so no work is shown per row.");
        foreach (var row in response.Aggregates)
            text.AppendLine($"    {row.Name}{(response.By == "rule" ? $" [{row.Kind}:{row.Key}]" : string.Empty)}: " +
                $"{RowWork(row, response.By, work, workTotal)}, {FormatCalls(row.Calls, row.Kind)}, " +
                $"{row.WordsTouched:N0} words, {row.SelfMs:N2} ms on this computer");
        var notAttributedWork = work.Where(row => row.IsUnattributed).Sum(row => row.ParserWork);
        var attribution = response.Attribution;
        if (work.Count > 0 || attribution.NotAttributedMs is not null)
            text.AppendLine("    Not attributed: " + (work.Count > 0
                    ? $"{Steps(notAttributedWork)} ({FormatShare(workTotal, notAttributedWork)})" : "parser work not recorded") +
                (attribution.NotAttributedMs is { } notAttributed ? $", {notAttributed:N2} ms on this computer" : string.Empty));
        if (attribution.Overrun)
            text.AppendLine($"  Warning: objects recorded {attribution.OverrunMs:N2} ms more than their words' own time on this computer.");
        text.AppendLine($"  Time on this computer: median {FormatMs(response.MedianMs)}  p95 {FormatMs(response.Percentile95Ms)}; " +
            $"{attribution.WordTimeMs:N2} ms in all for {attribution.MeasuredWordCount:N0} measured word(s)");
        if (response.CostliestWords.Count > 0)
        {
            text.AppendLine("  Costliest words:");
            foreach (var word in response.CostliestWords)
                text.AppendLine($"    {word.Word}: {FormatWordWork(word)}, {FormatCalls(word.Calls, null)}; time on this computer " +
                    $"{word.SelfMs:N2} ms" + (word.WordTimeMs is { } whole ? $" of its {whole:N2} ms" : string.Empty));
        }
        return text.ToString();
    }

    /// <summary>The Overview's speed lines: work first, then the run's time on this computer.</summary>
    public static void AppendOverviewSpeed(StringBuilder text, OverviewTiming timing)
    {
        var slowest = timing.SlowestWords.FirstOrDefault();
        var slowestText = slowest is null ? "costliest (none)"
            : $"costliest {slowest.Word} {(slowest.ParserWork is { } slowestWork ? Steps(slowestWork) : "parser work not recorded")}";
        text.AppendLine(timing.Work.MeasuredWordCount == 0
            ? "Work       parser work not recorded for these words"
            : $"Work       {Steps(timing.Work.TotalParserWork)} in all for {timing.Work.MeasuredWordCount:N0} words  " +
              $"median {Steps(timing.Work.MedianParserWork)}  p95 {Steps(timing.Work.Percentile95ParserWork)}  {slowestText}");
        if (timing.Work.MeasuredWordCount > 0)
            text.AppendLine("           " + (timing.ParserWorkAggregates.Count == 0
                ? "work by kind not recorded for every word"
                : "work by kind: " + string.Join(", ", WorkSplit(timing.ParserWorkAggregates, timing.Work.TotalParserWork))));
        text.AppendLine($"Time on this computer     median {FormatMs(timing.MedianMs)}  p95 {FormatMs(timing.Percentile95Ms)}");
        if (timing.Attribution.MeasuredWordCount > 0)
            text.AppendLine($"           {timing.Attribution.MeasuredWordCount:N0} words, {timing.Attribution.WordTimeMs / 1000:N1} s in all");
        if (timing.ObjectTimingDetail is { IsComplete: false, Description: { } detail }) text.AppendLine(detail);
    }

    /// <summary>
    /// A costliest word's work in the word: what this rule did of all the work the word took. When the whole is not
    /// recorded or is the part itself, it says so rather than writing the same count twice.
    /// </summary>
    internal static string FormatWordWork(WordRuleTiming word)
    {
        if (word.ParserWork is not { } part) return "parser work not recorded";
        if (word.WordParserWork is not { } whole) return $"{Steps(part)} (the word's total is not recorded)";
        return part == whole && whole > 0 ? $"all of the word's {Steps(whole)}" : $"{part:N0} of its {Steps(whole)}";
    }

    private static string WorkLine(WordWorkSummary work) => work.MeasuredWordCount == 0
        ? "Parser work are not recorded for these words."
        : $"Work: {Steps(work.TotalParserWork)} in all for {work.MeasuredWordCount:N0} word(s); " +
          $"median {Steps(work.MedianParserWork)}, p95 {Steps(work.Percentile95ParserWork)} a word";

    // The work shares by kind, largest first, then the work no kind accounts for.
    private static IEnumerable<string> WorkSplit(IReadOnlyList<TimingWorkAggregate> work, long total)
    {
        var kinds = work.Where(row => !row.IsUnattributed).GroupBy(row => row.Kind)
            .Select(group => (Kind: group.Key, Steps: group.Sum(row => row.ParserWork)))
            .OrderByDescending(kind => kind.Steps).ThenBy(kind => kind.Kind, StringComparer.Ordinal);
        foreach (var kind in kinds) yield return $"{kind.Kind} {FormatShare(total, kind.Steps)}";
        var other = work.Where(row => row.IsUnattributed).Sum(row => row.ParserWork);
        if (other > 0) yield return $"not attributed {FormatShare(total, other)}";
    }

    private static string RowWork(TimingAggregateRow row, string by, IReadOnlyList<TimingWorkAggregate> work, long total)
    {
        if (work.Count == 0) return "parser work not recorded";
        var matches = by == "rule"
            ? work.Where(item => !item.IsUnattributed && ObjectIdentity.Same(item.TimingKey.Identity, row.TimingKey.Identity)).ToArray()
            : work.Where(item => !item.IsUnattributed && item.Kind == row.Key).ToArray();
        if (matches.Length == 0) return "parser work not recorded";
        var steps = matches.Sum(item => item.ParserWork);
        return $"{Steps(steps)} ({FormatShare(total, steps)})";
    }

    private static string Steps(double? count)
    {
        if (count is null) return "not recorded";
        var whole = (long)Math.Round(count.Value, MidpointRounding.AwayFromZero);
        return whole == 1 ? "1 parser work" : whole.ToString("N0", CultureInfo.CurrentCulture) + " parser work";
    }

    private static string FormatShare(long total, long part) => total > 0
        ? ((double)part / total).ToString("P1", CultureInfo.CurrentCulture) : "no work";

    private static string FormatCalls(long? calls, string? kind) => calls is { } value
        ? $"{value:N0} {(kind is { Length: > 0 } ? kind + " " : string.Empty)}calls" : "calls not counted";

    private static string FormatMs(double? value) => value is null ? "unknown" : value.Value.ToString("N0") + " ms";
}
