using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Tests.App;

internal static class SampleEvidence
{
    public static IReadOnlyList<AssessmentObjectTiming> ObjectTimings { get; } =
    [
        Timer("morph_rule", OverviewTimingScreenshots.SubjectAgreementRuleKey, "Subject agreement", "mwalimu", 230, 900),
        Timer("morph_rule", OverviewTimingScreenshots.SubjectAgreementRuleKey, "Subject agreement", "hawajafika", 34, 200),
        Timer("morph_rule", OverviewTimingScreenshots.SubjectAgreementRuleKey, "Subject agreement", "alikula", 10, 35),
        Timer("morph_rule", OverviewTimingScreenshots.SubjectAgreementRuleKey, "Subject agreement", "walikula", 6, 18),
        Timer("morph_rule", OverviewTimingScreenshots.SubjectAgreementRuleKey, "Subject agreement", "anapenda", 6, 18),
        Timer("morph_rule", OverviewTimingScreenshots.SubjectAgreementRuleKey, "Subject agreement", "watoto", 2, 6),
        Timer("morph_rule", "past-tense", "Past tense li-", "mwalimu", 140, 180),
        Timer("morph_rule", "past-tense", "Past tense li-", "alikula", 0.5, 4),
        Timer("morph_rule", "past-tense", "Past tense li-", "walikula", 2, 8),
        Timer("phon_rule", "vowel-harmony", "Vowel harmony", "mwalimu", 100, 200),
        Timer("phon_rule", "vowel-harmony", "Vowel harmony", "hawajafika", 10, 20),
        Timer("phon_rule", "vowel-harmony", "Vowel harmony", "chakula", 4, 8),
        Timer("phon_rule", "vowel-harmony", "Vowel harmony", "walikula", 3, 6),
        Timer("phon_rule", "vowel-harmony", "Vowel harmony", "alikula", 0.5, 2),
        Timer("phon_rule", "vowel-harmony", "Vowel harmony", "anapenda", 1, 2),
        Timer("phon_rule", "vowel-harmony", "Vowel harmony", "watoto", 1, 2),
        Timer("lex_entry", "teacher-entry", "walimu", "mwalimu", 40, 30),
        Timer("root_index", "roots", "Root lookup", "mwalimu", 32, 120),
    ];

    private static AssessmentObjectTiming Timer(string kind, string key, string name, string word, double ms, int calls) =>
        new(kind, key, Guid.TryParse(key, out _) ? "authored" : "structural", "synthesis", name, word,
            calls, null, (long)(ms * 1_000_000)) { Scope = Guid.TryParse(key, out _) ? null : "sample-grammar" };

    private static IReadOnlyList<AssessedWord> Measured(AssessCommandResponse assessment) =>
        assessment.Words.Select(word => new AssessedWord(word.Word, word.Outcome, [], word.ElapsedMs)
        {
            Morphology = word.Morphology, IsIncomplete = word.IsIncomplete, ProjectStanding = word.ProjectStanding,
            ReadingGrades = word.ReadingGrades,
        }).ToArray();

    public static TimingResponse Timing(AssessCommandResponse assessment, string by, TraceTimingKey? rule = null,
        IReadOnlyList<string>? selected = null)
    {
        var words = Measured(assessment).Where(word => selected is null || selected.Contains(word.Word)).ToArray();
        var summary = TimingAggregation.SummarizeWords(words);
        var aggregation = TimingAggregation.Aggregate(words, ObjectTimings, by, rule, 20);
        return new TimingResponse("assessment/one", "all", by, words.Length, summary.MedianMs, summary.Percentile95Ms,
            summary.SlowestWords, aggregation.Aggregates, aggregation.CostliestWords)
        {
            Words = words.Select(word => new TimingWordRow(word.Word, word.ElapsedMs,
                word.IsIncomplete ? TimingCompletion.StepLimit : TimingCompletion.Finished)
            {
                ElapsedNs = word.ElapsedMs * 1_000_000L,
                Origin = new WordMeasurementOrigin("assessment/one", "invocation/one",
                    DateTimeOffset.Parse("2026-09-22T10:18:00Z")),
            }).ToArray(),
            Attribution = aggregation.Attribution,
        };
    }

    public static OverviewResponse Overview(AssessCommandResponse assessment)
    {
        var words = assessment.Words;
        var comparisons = words.Select(CompareSemantics.Compare).ToArray();
        int Outcome(WordRowOutcome outcome) => comparisons.Count(row => row.Outcome == outcome);
        int Cell(string standing, WordRowOutcome outcome) => comparisons.Count(row => row.Standing == standing && row.Outcome == outcome);
        int Standing(string standing) => comparisons.Count(row => row.Standing == standing);
        var timing = TimingAggregation.SummarizeWords(Measured(assessment), ObjectTimings);
        var warnings = SeededGrammarFindings.All();
        var saved = DateTimeOffset.Parse("2026-09-22T09:18:00Z");
        var parsed = Outcome(WordRowOutcome.Same) + Outcome(WordRowOutcome.Different);
        return new OverviewResponse("Sample", saved, saved, words.Count, 2, 0, words.Count, 1318, 47, 862,
            "assessment/one", saved.AddHours(1), words.Sum(word => word.ElapsedMs) / 1000d, "grammar", "selection",
            new OverviewTextCoverage(parsed, Outcome(WordRowOutcome.NoParse), Outcome(WordRowOutcome.Stopped),
                Outcome(WordRowOutcome.NotParsed), words.Count, parsed)
            {
                SameWords = Outcome(WordRowOutcome.Same), DifferentWords = Outcome(WordRowOutcome.Different),
                OccurrenceCoveragePercent = 100d * parsed / words.Count,
            },
            new OverviewAccuracy(Cell(ProjectStanding.Approved, WordRowOutcome.Same), Standing(ProjectStanding.Approved),
                comparisons.Count(row => row.Tone == WordRowTone.Problem), Outcome(WordRowOutcome.Stopped),
                comparisons.Sum(row => row.RebuiltDisapproved.Count), Standing(ProjectStanding.Rejected),
                Cell(ProjectStanding.Candidate, WordRowOutcome.Same), Standing(ProjectStanding.Candidate))
            {
                ApprovedWordsNoMatch = Cell(ProjectStanding.Approved, WordRowOutcome.Different),
                ApprovedWordsNoParse = Cell(ProjectStanding.Approved, WordRowOutcome.NoParse),
                ApprovedWordsUnknown = Cell(ProjectStanding.Approved, WordRowOutcome.Stopped),
                ApprovedWordsSkipped = Cell(ProjectStanding.Approved, WordRowOutcome.NotParsed),
            }, timing, new OverviewWarningsSummary(warnings.Count, null, null, null)
            {
                ErrorCount = warnings.Count(warning => warning.Severity == GrammarDiagnosticLevel.Error),
                InformationCount = warnings.Count(warning => warning.Severity == GrammarDiagnosticLevel.Information),
            })
        {
            SelectionResolved = true, ProjectFileName = "Sample.fwdata", WordCoveragePercent = 100d * parsed / words.Count,
            BaselineCapturedUtc = saved.AddMinutes(42), BaselineSourceLastWriteUtc = saved,
            LookFirst = new OverviewLookFirst(words.Where(word => CompareSemantics.Compare(word) is
                    { Standing: ProjectStanding.Approved, Outcome: WordRowOutcome.NoParse }).Select(word => word.Word).ToArray(),
                [], words.Where(word => word.IsIncomplete).Select(word => word.Word).ToArray(),
                words.Where(word => word.IsIncomplete).Sum(word => word.ElapsedMs),
                Cell(ProjectStanding.Candidate, WordRowOutcome.Different)) { SharedLostMorphemesAvailable = true },
        };
    }
}
