using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Review;
using SIL.Motif.Worker.Runtime.Review;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Review;

/// <summary>Names the words a saved Trial lost or regressed, with their analyses before and after.</summary>
internal static class TrialRegressionReader
{
    private const int ReadingsPerSide = 3;

    private static readonly (ReviewFlagId Flag, string Cause)[] Causes =
    [
        (ReviewFlagId.NewWordFailures, "new-word-failures"),
        (ReviewFlagId.ConfirmedReadingsLost, "confirmed-readings-lost"),
    ];

    /// <summary>The Limitation code a report carries when its regressed words could not be read.</summary>
    internal const string UnavailableCode = "regressions-unavailable";

    /// <summary>The Limitation code a report carries when the Proposal overlap of its regressed words was not computed.</summary>
    internal const string OverlapNotComputedCode = "regression-overlap-not-computed";

    /// <summary>The reasons a word's Proposal overlap was not computed, kept apart from "no overlap".</summary>
    private sealed class OverlapNotes
    {
        public readonly SortedSet<string> Reasons = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Adds the regressed words to <paramref name="report"/>. An old or partly purged report that cannot give them
    /// keeps its summary and gains a Limitation saying so, because the words are an extra and never a reason to fail.
    /// A word whose overlap with the Proposal could not be computed has a null overlap and the report gains a Limitation
    /// saying why, so that it is never mistaken for a word no operation touches.
    /// </summary>
    internal static ReportView Attach(ReviewReportRepository repository, ReportView report, string proposalId,
        Func<RegressionContext?>? context = null)
    {
        var notes = new OverlapNotes();
        var attached = Attach(report, () => Read(repository, report, proposalId, context, notes));
        return notes.Reasons.Count == 0 ? attached : attached with
        {
            Limitations = [.. attached.Limitations, new Limitation(OverlapNotComputedCode, ["wordforms"],
                "The Proposal operations a regressed word overlaps were not computed for some words: " +
                string.Join("; ", notes.Reasons) + ". An absent overlap here does not mean no operation touches the word.", [])],
        };
    }

    /// <summary>As the repository overload, with the read supplied.</summary>
    internal static ReportView Attach(ReportView report, Func<TrialRegressions?> read)
    {
        try { return report with { Regressions = read() }; }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidDataException or IOException)
        {
            return report with { Limitations = [.. report.Limitations, new Limitation(UnavailableCode, ["wordforms"],
                "The regressed words could not be read from this report's saved word detail.", [])] };
        }
    }

    /// <summary>Null when no word regressed or the report keeps no word detail to read them from.</summary>
    private static TrialRegressions? Read(ReviewReportRepository repository, ReportView report, string proposalId,
        Func<RegressionContext?>? loadContext, OverlapNotes notes)
    {
        var context = new Lazy<RegressionContext?>(() =>
        {
            if (loadContext is null) { notes.Reasons.Add("no Proposal context was supplied"); return null; }
            try
            {
                var loaded = loadContext();
                if (loaded?.BaselineLimit is { } limit)
                    notes.Reasons.Add("the overlap is narrower because " + limit);
                return loaded;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                notes.Reasons.Add("the Proposal or Baseline could not be read (" + exception.GetType().Name + ": " + exception.Message + ")");
                return null;
            }
        });
        var reportId = new ReviewReportId(report.ReportId);
        var groups = new List<RegressionGroup>();
        try
        {
            foreach (var (flag, cause) in Causes)
            {
                var page = repository.ReadPage(new ReviewPageRequest(report.Pins.ProjectKey, reportId, "wordforms",
                    Flag: flag, Limit: TrialRegressions.WordsPerGroup));
                var total = page.TotalMatching ?? page.Items.Count;
                if (total == 0) continue;
                var words = page.Items.OfType<WordRow>().Select(row => Describe(repository, report, reportId, row, proposalId, context, notes)).ToArray();
                groups.Add(new RegressionGroup(cause, total, Math.Max(0, total - words.Length), words,
                    "motif proposal trial --project <fwdata> " + proposalId + " --detail full --section wordforms --flag " +
                    cause + " --limit 200 [--json]"));
            }
        }
        catch (ReviewDetailUnavailableException) { return null; }
        finally { if (context.IsValueCreated) context.Value?.Dispose(); }
        return groups.Count == 0 ? null : new TrialRegressions(groups);
    }

    /// <summary>The word in single quotes, so a shell reads <c>"</c>, <c>$</c>, a backtick or a quote in it as the letter.</summary>
    internal static string ShellQuote(string word) => "'" + word.Replace("'", "'\\''") + "'";

    private static RegressedWord Describe(ReviewReportRepository repository, ReportView report, ReviewReportId reportId,
        WordRow row, string proposalId, Lazy<RegressionContext?> context, OverlapNotes notes)
    {
        IReadOnlyList<ReadingRow> Side(Side side)
        {
            try
            {
                var page = repository.ReadChildren(new ReviewChildPageRequest(report.Pins.ProjectKey, reportId, "readings",
                    row.CaseKey, Limit: ReadingsPerSide) { Side = side });
                return page.Items.OfType<ReadingRow>().ToArray();
            }
            catch (ReviewDetailUnavailableException) { return []; }
        }
        var before = Side(Contract.Review.Side.Before);
        var after = Side(Contract.Review.Side.After);
        Func<string, string?>? entryOf = before.Count + after.Count == 0 || context.Value is not { } known ? null : known.EntryOf;
        RegressedReading Ids(ReadingRow reading) => new(Analysis(reading), [.. reading.Morphs.Select(morph => new RegressedMorph(
            Canonical(morph.FormId) is { } form && entryOf?.Invoke(form) is { } entry ? Canonical(entry) : null,
            Canonical(morph.MsaId), Canonical(morph.FormId)))]);
        var beforeIds = before.Select(Ids).ToArray();
        IReadOnlyList<TouchedByOperation>? touchedBy = null;
        if (beforeIds.Length == 0) notes.Reasons.Add("no Baseline readings were retained for some words");
        else if (context.Value is { } overlap) touchedBy = RegressionContext.Overlap(overlap, beforeIds);
        var quoted = ShellQuote(row.Word);
        return new RegressedWord(row.Word, row.CaseKey, row.Before.ObservedReadingCount, row.After.ObservedReadingCount,
            [.. before.Select(Analysis)], [.. after.Select(Analysis)], beforeIds, [.. after.Select(Ids)],
            "motif parse " + quoted + " --project <fwdata> --trace --at baseline [--json]",
            "motif parse " + quoted + " --project <fwdata> --trace --at " + proposalId + " [--json]", touchedBy);
    }

    private static string? Canonical(string? id) =>
        id is null ? null : RegressionContext.ToGuid(id) is { } guid ? CanonicalId.FromGuid(guid).ToString() : id;

    private static string Analysis(ReadingRow reading) =>
        string.Join(" + ", reading.Morphs.Select(morph => morph.Gloss.Length == 0 ? morph.Form : morph.Form + " (" + morph.Gloss + ")"));
}
