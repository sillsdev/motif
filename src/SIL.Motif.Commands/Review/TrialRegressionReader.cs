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

    /// <summary>
    /// Adds the regressed words to <paramref name="report"/>. An old or partly purged report that cannot give them
    /// keeps its summary and gains a Limitation saying so, because the words are an extra and never a reason to fail.
    /// </summary>
    internal static ReportView Attach(ReviewReportRepository repository, ReportView report, string proposalId) =>
        Attach(report, () => Read(repository, report, proposalId));

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
    internal static TrialRegressions? Read(ReviewReportRepository repository, ReportView report, string proposalId)
    {
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
                var words = page.Items.OfType<WordRow>().Select(row => Describe(repository, report, reportId, row)).ToArray();
                groups.Add(new RegressionGroup(cause, total, Math.Max(0, total - words.Length), words,
                    "motif proposal trial --project <fwdata> " + proposalId + " --detail full --section wordforms --flag " +
                    cause + " --limit 200 [--json]"));
            }
        }
        catch (ReviewDetailUnavailableException) { return null; }
        return groups.Count == 0 ? null : new TrialRegressions(groups);
    }

    private static RegressedWord Describe(ReviewReportRepository repository, ReportView report, ReviewReportId reportId, WordRow row)
    {
        IReadOnlyList<string> Side(Side side)
        {
            try
            {
                var page = repository.ReadChildren(new ReviewChildPageRequest(report.Pins.ProjectKey, reportId, "readings",
                    row.CaseKey, Limit: ReadingsPerSide) { Side = side });
                return page.Items.OfType<ReadingRow>().Select(Analysis).ToArray();
            }
            catch (ReviewDetailUnavailableException) { return []; }
        }
        return new RegressedWord(row.Word, row.CaseKey, row.Before.ObservedReadingCount, row.After.ObservedReadingCount,
            Side(Contract.Review.Side.Before), Side(Contract.Review.Side.After));
    }

    private static string Analysis(ReadingRow reading) =>
        string.Join(" + ", reading.Morphs.Select(morph => morph.Gloss.Length == 0 ? morph.Form : morph.Form + " (" + morph.Gloss + ")"));
}
