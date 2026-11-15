using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

internal static class SampleOverview
{
    internal static void AssertCaptureStopCounts(OverviewResponse overview, string scene)
    {
        Assert.True(overview.TextCoverage.UnknownWords >= overview.Timing.ParserWorkLimitedWordCount,
            $"Overview scene '{scene}' has more parser-work-limited words than generally stopped words.");
        Assert.True(overview.LookFirst.ParserWorkLimitedWords.Count == overview.Timing.ParserWorkLimitedWordCount,
            $"Overview scene '{scene}' reports {overview.Timing.ParserWorkLimitedWordCount} parser-work-limited words but " +
            $"lists {overview.LookFirst.ParserWorkLimitedWords.Count}.");
    }

    private const int TimedOutWordCount = 2;
    private static readonly string[] ParserWorkLimitedWords = ["mwalimu", "stopped-2", "stopped-3"];

    internal static OverviewResponse Populated() => new(
        "Sample", DateTimeOffset.Parse("2026-09-30T08:00:00Z"), DateTimeOffset.Parse("2026-09-30T06:11:00Z"),
        142, 2, 0, 611, 1318, 47, 862, "assessment/one", DateTimeOffset.Parse("2026-09-30T08:51:00Z"), 38,
        "sha256:" + new string('c', 64), "sha256:" + new string('d', 64),
        new OverviewTextCoverage(118, 17, ParserWorkLimitedWords.Length + TimedOutWordCount, 2, 611, 540)
        {
            SameWords = 81,
            DifferentWords = 37,
            OccurrenceCoveragePercent = 88.4,
        },
        new OverviewAccuracy(71, 84, 3, 2, 1, 3, 9, 14)
        {
            ApprovedWordsNoMatch = 3,
            ApprovedWordsNoParse = 8,
            ApprovedWordsUnknown = 2,
        },
        new OverviewTiming(6.4, 48.2, [new SlowWordTiming("mwalimu", 700) { ParserWork = 7_000 },
                new SlowWordTiming("hawajafika", 48) { ParserWork = 480 }],
            ParserWorkLimitedWords.Length)
        {
            MeasuredWordCount = 142,
            Work = new WordWorkSummary(142, 380_000, 64, 482),
            ParserWorkAggregates =
            [
                new TimingWorkAggregate("morph_rule", "morph_rule", "morph_rule", 212_800, false),
                new TimingWorkAggregate("phon_rule", "phon_rule", "phon_rule", 79_800, false),
                new TimingWorkAggregate("lex_entry", "lex_entry", "lex_entry", 26_600, false),
                new TimingWorkAggregate("root_index", "root_index", "root_index", 15_200, false),
                new TimingWorkAggregate("unattributed", "Not attributed", "unattributed", 45_600, true),
            ],
            Kinds =
            [
                new TimingAggregateRow("morph_rule", "morph_rule", 21280, 0.56, 142) { Kind = "morph_rule" },
                new TimingAggregateRow("phon_rule", "phon_rule", 7980, 0.21, 142) { Kind = "phon_rule" },
                new TimingAggregateRow("lex_entry", "lex_entry", 2660, 0.07, 142) { Kind = "lex_entry" },
                new TimingAggregateRow("root_index", "root_index", 1520, 0.04, 142) { Kind = "root_index" },
            ],
            Attribution = new WordTimeAttribution(142, 38000, 33440, 4560, 0.12, 0, false),
        },
        new OverviewWarningsSummary(24, 20, "Grammar warning", 20)
        {
            ErrorCount = 0,
            WarningCount = 20,
            InformationCount = 4,
            YourWords = new WarningWordsTouched(14, 7, []) { BySpellingOnly = 3 },
            ByKind =
            [
                new GrammarWarningSummary("test.finding", "Grammar warning", GrammarDiagnosticLevel.Warning, 6)
                {
                    YourWords = 4,
                    BySpellingOnly = 2,
                },
            ],
        })
    {
        LookFirst = new OverviewLookFirst(
            ["lost-1", "lost-2", "lost-3", "lost-4", "lost-5", "lost-6"],
            [new OverviewSharedMorpheme("kat", 3, true), new OverviewSharedMorpheme("ja-", 3, false)],
            ParserWorkLimitedWords, 33600, 3)
        { SharedLostMorphemesAvailable = true, ParserWorkLimitedWordParserWork = 336_000 },
        SelectionResolved = true,
        WordCoveragePercent = 83.1,
        ProjectFileName = "Sample.fwdata",
        BaselineCapturedUtc = DateTimeOffset.Parse("2026-09-22T06:00:00Z"),
        BaselineSourceLastWriteUtc = DateTimeOffset.Parse("2026-09-22T05:40:00Z"),
    };

}
