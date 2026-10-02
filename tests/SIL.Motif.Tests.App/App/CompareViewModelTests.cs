using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;
using SIL.Motif.Commands.Queries;

namespace SIL.Motif.Tests.App;

public sealed class CompareViewModelTests
{
    private static AssessmentWordResult Word(
        string word, string outcome, string standing, IReadOnlyList<string>? grades = null,
        bool incomplete = false, int missedApproved = 0, int? occurrences = null)
    {
        var analyses = grades?.Select((_, index) => new ParseAnalysis(
            [new ParseMorph($"{word}-{index}", "n", null, null)])).ToArray() ?? [];
        var stored = grades?.Select((grade, index) => (grade, index))
            .Where(item => item.grade is ReadingGrade.Approved or ReadingGrade.Candidate or ReadingGrade.Disapproved)
            .Select(item => StoredFor($"{word}-{item.index}", item.grade)).ToList() ?? [];
        for (var index = 0; index < missedApproved; index++)
            stored.Add(StoredFor($"{word}-missing-{index}", ReadingGrade.Approved));
        return WithPriority(new AssessmentWordResult(word, outcome, incomplete, "Search completed", 10, null)
        {
            Readings = grades?.Select(grade => Reading(grade)).ToArray(),
            ReadingGrades = grades,
            StoredAnalyses = stored,
            MissedApproved = Enumerable.Range(0, missedApproved).Select(_ => Reading("missed")).ToArray(),
            Morphology = grades is null ? null : new ParseWordEvidence("v1", 0, word, 10,
                false, false, false, analyses, []),
            ProjectStanding = standing,
            OccurrenceCount = occurrences,
        });
    }

    private static ParserReading StoredFor(string form, string opinion) =>
        new([new ParserReadingMorph(form, "gloss", "n", null, false, null)])
        {
            StoredAnalysisId = "stored-" + form,
            StoredAnalysisOpinion = opinion,
            Identity = new ApprovedMorphology([new ApprovedMorph(form, "n", null, ["entry"])]),
        };
    private static AssessmentWordResult WithPriority(AssessmentWordResult word) => word with
    {
        FixFirst = CompareSemantics.FixFirst(new CompareWordFacts(
            word.ProjectStanding, word.Outcome, word.IsIncomplete, word.Morphology,
            word.ReadingGrades, word.MissedApproved?.Count ?? 0), word.MissedApproved),
    };

    private static ParserReading Reading(string gloss) =>
        new([new ParserReadingMorph("form", gloss, "n", null, false, null)]);

    private static (WordProjectStatus, CompareColumnKind) PlaceOne(AssessmentWordResult word) =>
        CompareViewModel.Place(new AssessWordRowViewModel(word));

    [Theory]
    [InlineData(ProjectStanding.Candidate, false)]
    [InlineData(ProjectStanding.Approved, false)]
    [InlineData(ProjectStanding.Candidate, true)]
    [InlineData(ProjectStanding.Approved, true)]
    public void MixedOpinionsAndPartialSearchShareOneComparisonAcrossMatrixRowsAndTexts(string standing, bool incomplete)
    {
        var opinion = standing == ProjectStanding.Approved ? ReadingGrade.Approved : ReadingGrade.Candidate;
        var word = Word("mixed", "analysed", standing, [ReadingGrade.Disapproved, ReadingGrade.NoOpinion], incomplete);
        word = word with { StoredAnalyses = [.. word.StoredAnalyses, StoredFor("undecided", opinion)] };
        var row = new AssessWordRowViewModel(word);
        var matrix = new CompareViewModel();
        matrix.Load([row]);
        var token = new TextToken("mixed", "mixed", null, null)
        {
            StoredAnalyses = word.StoredAnalyses.Select(analysis => new ProjectAnalysis("", analysis.Morphs)
            {
                StoredAnalysisId = analysis.StoredAnalysisId, StoredAnalysisOpinion = analysis.StoredAnalysisOpinion,
                Identity = analysis.Identity,
            }).ToArray(),
        };
        var text = new ResultsTokenViewModel("Text", 1, token, word);
        var expectedOutcome = incomplete ? WordRowOutcome.Stopped : WordRowOutcome.Different;

        Assert.Equal(expectedOutcome, row.Comparison.Outcome);
        Assert.Equal(expectedOutcome, row.WordRow.Row.Outcome);
        Assert.Equal(row.WordRow.Meaning, Assert.Single(matrix.Words).Meaning);
        Assert.Equal(row.Comparison.MeaningCode, text.Comparison.MeaningCode);
        Assert.Equal(row.Comparison.Outcome, text.Comparison.Outcome);
        Assert.Equal(row.Comparison.Readings[0].Matches, text.Comparison.Readings[0].Matches);
        Assert.Equal(incomplete ? "No result" : row.WordRow.Meaning, Assert.Single(matrix.Words).Meaning);
        Assert.Equal(ReadingGrade.Disapproved, Assert.Single(row.Comparison.RebuiltDisapproved).Opinion);
        Assert.Equal([1], row.Comparison.ExtraReadingIndices);
        Assert.Equal(incomplete ? 0 : opinion == ReadingGrade.Approved ? 1 : 0, row.Comparison.MissingApproved.Count);
        if (incomplete)
        {
            Assert.Empty(row.Comparison.UndecidedNotBuilt);
            Assert.True(text.IsPanGlossCapped);
            Assert.Equal("No result", row.WordRow.Meaning);
        }
        else
        {
            Assert.Equal(incomplete ? "Unknown yet" : opinion == ReadingGrade.Approved
                ? "Built something else" : "Differs: have a look", text.PanGlossSummary);
            Assert.False(matrix.ShowsMeaning);
            if (opinion == ReadingGrade.Candidate)
                Assert.Contains("Your undecided analysis wasn't built", row.WordRow.MeaningDetail);
        }
    }

    [Fact]
    public void UndecidedWordWithOnlyItsDisapprovedAnalysisRebuiltIsDifferent()
    {
        var word = Word("mixed", "analysed", ProjectStanding.Candidate, [ReadingGrade.Disapproved]);
        word = word with
        {
            StoredAnalyses = [.. word.StoredAnalyses, StoredFor("undecided", ReadingGrade.Candidate)],
        };
        var row = new AssessWordRowViewModel(word);
        var matrix = new CompareViewModel();
        matrix.Load([row]);

        Assert.Equal((WordProjectStatus.Candidate, CompareColumnKind.NoMatch), CompareViewModel.Place(row));
        Assert.Equal("Differs: have a look", Assert.Single(matrix.Words).Meaning);
        Assert.Equal(row.WordRow.Meaning, Assert.Single(matrix.Words).Meaning);
    }

    [Fact]
    public void AnApprovedWordIsKeptOnlyWhenEveryApprovedAnalysisWasRebuilt()
    {
        Assert.Equal((WordProjectStatus.Approved, CompareColumnKind.Match),
            PlaceOne(Word("kitabu", "analysed", ProjectStanding.Approved, ["approved"])));
        // One approved analysis rebuilt and another missed is not Kept: every approved morphology is expected.
        Assert.Equal((WordProjectStatus.Approved, CompareColumnKind.NoMatch),
            PlaceOne(Word("walikula", "analysed", ProjectStanding.Approved, ["approved"], missedApproved: 1)));
        Assert.Equal((WordProjectStatus.Approved, CompareColumnKind.NoParse),
            PlaceOne(Word("hawajafika", "no-analysis", ProjectStanding.Approved, missedApproved: 1)));
    }

    [Fact]
    public void ALimitIsATimeoutEvenAfterReadingsAndASkippedWordIsNotTested()
    {
        Assert.Equal((WordProjectStatus.Approved, CompareColumnKind.Timeout),
            PlaceOne(Word("alimpiga", "analysed", ProjectStanding.Approved, ["approved"], incomplete: true)));
        Assert.Equal((WordProjectStatus.NotPresent, CompareColumnKind.Skipped),
            PlaceOne(Word("x y", "skipped", ProjectStanding.NotPresent)));
    }

    [Theory]
    [InlineData(ProjectStanding.Candidate, "analysed", "candidate", CompareColumnKind.Match)]
    [InlineData(ProjectStanding.Candidate, "analysed", "no-opinion", CompareColumnKind.NoMatch)]
    [InlineData(ProjectStanding.Rejected, "analysed", "disapproved", CompareColumnKind.Match)]
    [InlineData(ProjectStanding.Rejected, "analysed", "no-opinion", CompareColumnKind.NoMatch)]
    [InlineData(ProjectStanding.NotPresent, "analysed", "no-opinion", CompareColumnKind.NoMatch)]
    [InlineData(ProjectStanding.IncorrectSpelling, "analysed", "approved", CompareColumnKind.Match)]
    [InlineData(ProjectStanding.IncorrectSpelling, "no-analysis", null, CompareColumnKind.NoParse)]
    [InlineData(ProjectStanding.IncorrectSpelling, "capped", null, CompareColumnKind.Timeout)]
    public void AParsedWordMatchesWhenTheParserBuiltWhatItsRowHolds(
        string standing, string outcome, string? grade, CompareColumnKind expected) =>
        Assert.Equal(expected, PlaceOne(Word("w", outcome, standing, grade is null ? null : [grade])).Item2);

    [Fact]
    public void MatrixAndRecordedPlacementAgreeAcrossStandingAndOutcomeCases()
    {
        var cases = new (string Outcome, string[]? Grades, bool Incomplete, int MissedApproved)[]
        {
            ("analysed", null, false, 0),
            ("analysed", [], false, 0),
            ("analysed", [ReadingGrade.Approved], false, 0),
            ("analysed", [ReadingGrade.Candidate], false, 0),
            ("analysed", [ReadingGrade.Disapproved], false, 0),
            ("analysed", [ReadingGrade.NoOpinion], false, 0),
            ("analysed", [ReadingGrade.Approved, ReadingGrade.NoOpinion], false, 0),
            ("analysed", [ReadingGrade.Approved], false, 1),
            ("analysed", [ReadingGrade.Disapproved, ReadingGrade.NoOpinion], false, 0),
            ("analysed", [ReadingGrade.Approved], true, 0),
            ("no-analysis", null, false, 0),
            ("capped", [ReadingGrade.Approved], true, 0),
            ("skipped", null, false, 0),
        };
        foreach (var standing in new[] { ProjectStanding.Approved, ProjectStanding.Candidate,
                     ProjectStanding.Rejected, ProjectStanding.NotPresent, ProjectStanding.IncorrectSpelling })
        foreach (var (outcome, grades, incomplete, missedApproved) in cases)
        {
            if (outcome == "analysed" && grades is { Length: > 0 } && (standing switch
                {
                    ProjectStanding.Approved => !grades.Contains(ReadingGrade.Approved),
                    ProjectStanding.Candidate => !grades.Contains(ReadingGrade.Candidate),
                    ProjectStanding.Rejected => !grades.Contains(ReadingGrade.Disapproved),
                    ProjectStanding.NotPresent => grades.Any(grade => grade != ReadingGrade.NoOpinion),
                    _ => false,
                })) continue;
            var word = Word("w", outcome, standing, grades, incomplete, missedApproved);
            var matrix = PlaceOne(word).Item2;
            var recorded = CompareSemantics.Place(new CompareWordFacts(standing, outcome, incomplete,
                word.Morphology, grades, missedApproved)).Column;
            Assert.True(matrix == recorded,
                $"{standing}/{outcome}/{string.Join(',', grades ?? [])}/{incomplete}/{missedApproved}: " +
                $"Matrix {matrix}, recorded {recorded}");
        }
    }
    [Fact]
    public void EveryCellMeansWhatTheGridSaysAndTimeoutsAreNeverViolations()
    {
        Assert.Equal(CompareFamilyKind.Violation, CompareViewModel.MeaningOf(WordProjectStatus.Approved, CompareColumnKind.NoParse).Family);
        Assert.Equal(CompareFamilyKind.Violation, CompareViewModel.MeaningOf(WordProjectStatus.Rejected, CompareColumnKind.Match).Family);
        Assert.Equal(CompareFamilyKind.New, CompareViewModel.MeaningOf(WordProjectStatus.NotPresent, CompareColumnKind.NoMatch).Family);
        Assert.Equal("Nobody can analyze",
            CompareViewModel.MeaningOf(WordProjectStatus.NotPresent, CompareColumnKind.NoParse).Label);
        foreach (var row in Enum.GetValues<WordProjectStatus>())
            Assert.Equal(CompareFamilyKind.Unknown, CompareViewModel.MeaningOf(row, CompareColumnKind.Timeout).Family);
    }

    [Fact]
    public void NobodyPresetUsesTheAnalyzeSpelling()
    {
        var compare = new CompareViewModel();

        Assert.Contains(compare.Presets, preset => preset.Label == "Nobody can analyze");
    }

    [Fact]
    public void AWordMissingFromTheProjectUsesTheNotInFieldWorksLabel()
    {
        Assert.Equal("Not in FieldWorks", CompareViewModel.RowLabelOf(WordProjectStatus.NotPresent));
    }

    [Fact]
    public void MatrixCellAutomationNameUsesTheOpinionAndPanGlossSentence()
    {
        var cell = new CompareCellViewModel(WordProjectStatus.Approved, CompareColumnKind.Match);
        cell.SetCounts(12, 30);

        Assert.Equal("12 words, 30 places: Approved in FieldWorks, PanGloss finds the same", cell.AccessibleName);
    }

    [Theory]
    [InlineData(WordProjectStatus.Approved, OpinionMarkKind.Approved)]
    [InlineData(WordProjectStatus.Candidate, OpinionMarkKind.Unknown)]
    [InlineData(WordProjectStatus.Rejected, OpinionMarkKind.Disapproved)]
    [InlineData(WordProjectStatus.NotPresent, OpinionMarkKind.None)]
    [InlineData(WordProjectStatus.IncorrectSpelling, OpinionMarkKind.None)]
    public void MatrixRowsExposeTheirRoundFourOpinionMark(WordProjectStatus row, OpinionMarkKind expected) =>
        Assert.Equal(expected, CompareViewModel.OpinionMarkFor(row));

    [Theory]
    [InlineData(CompareColumnKind.Match, "Same")]
    [InlineData(CompareColumnKind.NoMatch, "Different")]
    [InlineData(CompareColumnKind.NoParse, "No parse")]
    [InlineData(CompareColumnKind.Timeout, "Stopped")]
    [InlineData(CompareColumnKind.Skipped, "Not parsed")]
    public void MatrixColumnsUsePanGlossAgreementLanguage(CompareColumnKind column, string expected) =>
        Assert.Equal(expected, CompareViewModel.ColumnLabelOf(column));

    [Fact]
    public void MatrixLegendListsTheRoundFourMarksAndPanGlossClasses()
    {
        var compare = new CompareViewModel();

        Assert.Equal(["Approved", "Unknown", "Disapproved", "Not in FieldWorks"],
            compare.OpinionLegend.Select(item => item.Label));
        Assert.Equal(["Same", "Different", "No parse", "Stopped", "Not parsed"],
            compare.PanGlossLegend.Select(item => item.Label));
        Assert.False(compare.PanGlossLegend.Single(item => item.Kind == AnalysisMarkingClass.Different).IsConflict);
        Assert.True(compare.PanGlossLegend.Single(item => item.Kind == AnalysisMarkingClass.Different).IsDifferent);
    }

    [Fact]
    public void MissingProjectRowUsesOneLabelAcrossItsHeaderAndLegend()
    {
        var compare = new CompareViewModel();

        Assert.Equal("Not in FieldWorks", CompareViewModel.OpinionLabelOf(WordProjectStatus.NotPresent));
        Assert.Equal("Not in FieldWorks", compare.OpinionLegend.Single(item => item.Kind == OpinionMarkKind.None).Label);
    }

    [Theory]
    [InlineData(AnalysisMarkingClass.Same, "Same")]
    [InlineData(AnalysisMarkingClass.Conflict, "Different")]
    [InlineData(AnalysisMarkingClass.Different, "Different")]
    [InlineData(AnalysisMarkingClass.Extra, "Different, and more")]
    [InlineData(AnalysisMarkingClass.None, "No parse")]
    [InlineData(AnalysisMarkingClass.Capped, "Stopped")]
    [InlineData(AnalysisMarkingClass.NotAssessed, "Not parsed")]
    public void CompactWordUsesEachPanGlossClassLabel(AnalysisMarkingClass markingClass, string expected) =>
        Assert.Equal(expected, CompareViewModel.PanGlossClassLabel(markingClass));

    [Fact]
    public void ListedWordUsesTheSharedAnalysisMarkingState()
    {
        var stored = StoredReading("analysis-1", ReadingGrade.Approved);
        var result = new AssessmentWordResult("kitabu", "analysed", false, "Search completed", 10, null)
        {
            ProjectStanding = ProjectStanding.Approved,
            Readings = [new ParserReading(stored.Morphs)],
            StoredAnalyses = [stored],
            ReadingGrades = [ReadingGrade.Approved],
            Morphology = new ParseWordEvidence("v1", 0, "kitabu", 10,
                false, false, false, [new ParseAnalysis([new ParseMorph("kitabu", "n", null, null)])], []),
        };
        var row = new AssessWordRowViewModel(result);
        var word = new CompareWordViewModel(row,
            (WordProjectStatus.Approved, CompareColumnKind.Match));

        Assert.Equal(AnalysisMarkingClass.Same, word.Marking.PanGlossClass);
        Assert.Equal(OpinionMarkKind.Approved, word.OpinionMark);
    }

    [Fact]
    public void MatrixAndListsPlaceAnExtraReadingWithTheStripInsteadOfCallingItKept()
    {
        var stored = StoredReading("analysis-1", ReadingGrade.Approved);
        var result = new AssessmentWordResult("kitabu", "analysed", false, "Search completed", 10, null)
        {
            ProjectStanding = ProjectStanding.Approved,
            StoredAnalyses = [stored],
            ReadingGrades = [ReadingGrade.Approved, ReadingGrade.NoOpinion],
            Morphology = new ParseWordEvidence("v1", 0, "kitabu", 10,
                false, false, false,
                [new ParseAnalysis([new ParseMorph("kitabu", "n", null, null)]),
                    new ParseAnalysis([new ParseMorph("other", "n", null, null)])], []),
        };
        var row = new AssessWordRowViewModel(result);
        var compare = new CompareViewModel();
        compare.Load([row]);
        var lists = new TextsListsViewModel(compare);

        Assert.Equal(AnalysisMarkingClass.Extra, row.Marking.PanGlossClass);
        Assert.Equal((WordProjectStatus.Approved, CompareColumnKind.NoMatch), CompareViewModel.Place(row));
        Assert.Contains("kitabu", lists.Lists.Single(list => list.Name == "Built something else")
            .Cells.SelectMany(cell => compare.WordsInCells([cell])));
    }
    [Fact]
    public void MatrixTreatsRebuiltDisapprovedMorphologyAsSameDespiteTheOpinionConflict()
    {
        var stored = StoredReading("analysis-1", ReadingGrade.Disapproved);
        var result = new AssessmentWordResult("kitabu", "analysed", false, "Search completed", 10, null)
        {
            ProjectStanding = ProjectStanding.Rejected,
            StoredAnalyses = [stored],
            ReadingGrades = [ReadingGrade.Disapproved],
            Morphology = new ParseWordEvidence("v1", 0, "kitabu", 10,
                false, false, false,
                [new ParseAnalysis([new ParseMorph("kitabu", "n", null, null)])], []),
        };
        var row = new AssessWordRowViewModel(result);

        Assert.Equal(AnalysisMarkingClass.Conflict, row.Marking.PanGlossClass);
        Assert.Equal((WordProjectStatus.Rejected, CompareColumnKind.Match), CompareViewModel.Place(row));
    }
    [Fact]
    public void ListedWordShowsEveryStoredOpinionInsteadOfMergingThem()
    {
        var approved = StoredReading("approved-id", ReadingGrade.Approved);
        var disapproved = StoredReading("disapproved-id", ReadingGrade.Disapproved);
        var result = new AssessmentWordResult("kitabu", "analysed", false, "Search completed", 10, null)
        {
            ProjectStanding = ProjectStanding.Approved,
            Readings = [new ParserReading(approved.Morphs), new ParserReading(disapproved.Morphs)],
            StoredAnalyses = [approved, disapproved],
            ReadingGrades = [ReadingGrade.Approved, ReadingGrade.Disapproved],
            Morphology = new ParseWordEvidence("v1", 0, "kitabu", 10,
                false, false, false, [new ParseAnalysis([]), new ParseAnalysis([])], []),
        };
        var word = new CompareWordViewModel(new AssessWordRowViewModel(result),
            (WordProjectStatus.Approved, CompareColumnKind.NoMatch));

        Assert.Equal([OpinionMarkKind.Approved, OpinionMarkKind.Disapproved],
            word.OpinionMarks.Select(mark => mark.Kind));
    }

    [Theory]
    [InlineData(ReadingGrade.Approved, OpinionMarkKind.Approved)]
    [InlineData(ReadingGrade.Candidate, OpinionMarkKind.Unknown)]
    [InlineData(ReadingGrade.NoOpinion, OpinionMarkKind.Unknown)]
    [InlineData(ReadingGrade.Disapproved, OpinionMarkKind.Disapproved)]
    public void ListedWordShowsTheOpinionRecordedForItsStoredAnalysis(string opinion, OpinionMarkKind expected)
    {
        var stored = StoredReading("analysis-1", opinion);
        var result = new AssessmentWordResult("kitabu", "no-analysis", false, "Search completed", 10, null)
        {
            ProjectStanding = ProjectStanding.Approved,
            StoredAnalyses = [stored],
        };
        var word = new CompareWordViewModel(new AssessWordRowViewModel(result),
            (WordProjectStatus.Approved, CompareColumnKind.NoParse));

        Assert.Equal(expected, Assert.Single(word.OpinionMarks).Kind);
    }

    [Fact]
    public void ListedWordWithoutStoredAnalysesShowsTheDashedNoneMark()
    {
        var result = new AssessmentWordResult("motifextra", "analysed", false, "Search completed", 10, null)
        {
            ProjectStanding = ProjectStanding.NotPresent,
        };
        var word = new CompareWordViewModel(new AssessWordRowViewModel(result),
            (WordProjectStatus.NotPresent, CompareColumnKind.Match));

        Assert.Equal(OpinionMarkKind.None, Assert.Single(word.OpinionMarks).Kind);
    }

    // Without its analyses in hand the row's opinion still holds, so the mark must not claim "Not in FieldWorks".
    [Theory]
    [InlineData(WordProjectStatus.Approved, OpinionMarkKind.Approved, "Approved")]
    [InlineData(WordProjectStatus.Candidate, OpinionMarkKind.Unknown, "Unknown")]
    [InlineData(WordProjectStatus.Rejected, OpinionMarkKind.Disapproved, "Disapproved")]
    public void ListedWordWithoutItsAnalysesInHandWearsItsRowsMark(WordProjectStatus row, OpinionMarkKind kind, string label)
    {
        var result = new AssessmentWordResult("alikula", "analysed", false, "Search completed", 10, null);
        var word = new CompareWordViewModel(new AssessWordRowViewModel(result), (row, CompareColumnKind.NoMatch));

        var mark = Assert.Single(word.OpinionMarks);
        Assert.Equal(kind, mark.Kind);
        Assert.Equal(label, mark.Label);
    }

    // Lists draws each reading as the interlinear morpheme row used elsewhere, not as "a- + li- = 3SG + PST".
    [Fact]
    public void AListedWordOffersEachParserReadingAsMorphemes()
    {
        var result = new AssessmentWordResult("alikula", "analysed", false, "Search completed", 10, null)
        {
            Readings =
            [
                new ParserReading([
                    new ParserReadingMorph("a-", "3SG", null, null, false, null),
                    new ParserReadingMorph("kul", "eat", "v", null, false, null),
                ]),
            ],
        };
        var word = new CompareWordViewModel(new AssessWordRowViewModel(result),
            (WordProjectStatus.Approved, CompareColumnKind.NoMatch));

        var reading = Assert.Single(word.Readings);
        Assert.Equal(["a-", "kul"], reading.Morphs.Select(morph => morph.Form));
        Assert.Equal(["3SG", "eat"], reading.Morphs.Select(morph => morph.Gloss));
    }

    private static ParserReading StoredReading(string id, string opinion) =>
        new([new ParserReadingMorph("kitabu", "book", "n", null, false, null)])
        {
            StoredAnalysisId = id,
            StoredAnalysisOpinion = opinion,
            Identity = new ApprovedMorphology([new ApprovedMorph("kitabu", "n", null, ["entry"])])
            {
                SourceAnalysisId = id,
                SourceWordformGuid = "wordform-1",
            },
        };

    private static readonly AssessmentWordResult[] Sample =
    [
        Word("kitabu", "analysed", ProjectStanding.Approved, ["approved"]),
        Word("walikula", "analysed", ProjectStanding.Approved, ["no-opinion"], missedApproved: 1),
        Word("hawajafika", "no-analysis", ProjectStanding.Approved, missedApproved: 1),
        Word("kitanda", "analysed", ProjectStanding.Rejected, ["disapproved"]),
        Word("chakula", "analysed", ProjectStanding.Candidate, ["candidate"]),
        Word("mwalimu", "analysed", ProjectStanding.NotPresent, ["no-opinion"]),
        Word("ninakupenda", "no-analysis", ProjectStanding.NotPresent),
        Word("alimpiga", "timed-out", ProjectStanding.NotPresent, incomplete: true),
        Word("x y", "skipped", ProjectStanding.NotPresent),
    ];

    private static (AssessWordsViewModel Table, CompareViewModel Compare) Loaded()
    {
        var table = new AssessWordsViewModel();
        table.Load(Sample);
        var compare = new CompareViewModel();
        compare.Load(table.AllRows);
        return (table, compare);
    }

    [Fact]
    public void TheMatrixCountsEveryWordOnceAndItsColumnsAgreeWithTheOutcomeBar()
    {
        var (table, compare) = Loaded();

        Assert.Equal(Sample.Length, compare.Cells.Sum(cell => cell.Count));
        Assert.Equal(Sample.Length, compare.Words.Count);
        int Column(CompareColumnKind column) => compare.Columns.Single(item => item.Column == column).Count;
        int Outcome(Mark mark) => table.Outcomes.Where(segment => segment.Mark == mark).Sum(segment => segment.Count);
        Assert.Equal(Outcome(Mark.Same), Column(CompareColumnKind.Match) + Column(CompareColumnKind.NoMatch));
        Assert.Equal(Outcome(Mark.NoParse), Column(CompareColumnKind.NoParse));
        Assert.Equal(Outcome(Mark.Stopped), Column(CompareColumnKind.Timeout));
        Assert.Equal(Outcome(Mark.NotParsed), Column(CompareColumnKind.Skipped));
    }

    [Fact]
    public void TheLostPresetListsExactlyTheApprovedWordsWithNoParse()
    {
        var (_, compare) = Loaded();

        compare.SelectPresetCommand.Execute(compare.Presets.Single(preset => preset.Label == "Lost"));

        Assert.Equal(["hawajafika"], compare.Words.Select(word => word.Word));
        Assert.True(compare.Presets.Single(preset => preset.Label == "Lost").IsActive);
        Assert.Equal("1 word", compare.ListSummary);
    }

    [Fact]
    public void AClickChoosesOneCellCtrlClickAddsAndClickingTheOnlyChoiceClearsIt()
    {
        var (_, compare) = Loaded();
        var kept = compare.Cells.Single(cell => cell.Row == WordProjectStatus.Approved && cell.Column == CompareColumnKind.Match);
        var proposes = compare.Cells.Single(cell => cell.Row == WordProjectStatus.NotPresent && cell.Column == CompareColumnKind.NoMatch);

        compare.Toggle(kept, additive: false);
        Assert.Equal(["kitabu"], compare.Words.Select(word => word.Word));

        compare.Toggle(proposes, additive: true);
        Assert.Equal(["kitabu", "mwalimu"], compare.Words.Select(word => word.Word).Order());

        compare.Toggle(proposes, additive: false);
        Assert.Equal(["mwalimu"], compare.Words.Select(word => word.Word));

        compare.Toggle(proposes, additive: false);
        Assert.False(compare.AnySelected);
        Assert.Equal(9, compare.Words.Count);
    }

    [Fact]
    public void SearchingNarrowsTheListButNeverTheMatrix()
    {
        var (_, compare) = Loaded();
        compare.SelectRowCommand.Execute(compare.Rows.Single(row => row.Row == WordProjectStatus.Approved));

        compare.SearchText = "kit";

        Assert.Equal(["kitabu"], compare.Words.Select(word => word.Word));
        Assert.Equal(3, compare.Rows.Single(row => row.Row == WordProjectStatus.Approved).Count);
        Assert.Equal(9, compare.Cells.Sum(cell => cell.Count));
    }

    [Fact]
    public void OpeningAListedWordHandsItToTheWordsView()
    {
        var (_, compare) = Loaded();
        string? opened = null;
        compare.OpenWord = word => opened = word;

        compare.OpenWordCommand.Execute(compare.Words.First(word => word.Word == "chakula"));

        Assert.Equal("chakula", opened);
    }

    [Fact]
    public void AnAssessmentThatDidNotReadTheProjectSaysSoRatherThanFillingNotPresent()
    {
        var table = new AssessWordsViewModel();
        table.Load([new AssessmentWordResult("kitabu", "analysed", false, "Search completed", 10, null)]);
        var compare = new CompareViewModel();

        compare.Load(table.AllRows);

        Assert.False(compare.HasStandings);
    }

    [Fact]
    public void ACellShowsItsWordsAndTheirPlacesTogetherSoNothingHidesBehindAToggle()
    {
        var table = new AssessWordsViewModel();
        table.Load([
            Word("common", "no-analysis", ProjectStanding.Approved, occurrences: 8),
            Word("rare", "no-analysis", ProjectStanding.Approved, occurrences: 1),
            Word("once", "analysed", ProjectStanding.Approved, ["approved"], occurrences: 1),
        ]);
        var compare = new CompareViewModel();
        compare.Load(table.AllRows);
        var lost = Cell(compare, WordProjectStatus.Approved, CompareColumnKind.NoParse);
        var kept = Cell(compare, WordProjectStatus.Approved, CompareColumnKind.Match);

        Assert.Equal("2 words · 9 places", lost.CountText);
        Assert.Equal("9 places", lost.PlacesText);
        Assert.Equal("1 place", kept.PlacesText);
        Assert.True(lost.ShowsPlaces);
        Assert.False(Cell(compare, WordProjectStatus.Rejected, CompareColumnKind.Match).ShowsPlaces);
        Assert.Equal("2 words, 9 places: Approved in FieldWorks, PanGloss found no parse", lost.AccessibleName);
        Assert.Equal(3, compare.Rows.Single(row => row.Row == WordProjectStatus.Approved).Count);
        Assert.Equal(2, compare.Presets.Single(preset => preset.Label == "Lost").Count);
        Assert.Null(typeof(CompareViewModel).GetProperty("CountMode"));
    }

    [Fact]
    public void ThePresetsAreTheNamedListsInTheMatrixsOwnWords()
    {
        var compare = new CompareViewModel();

        Assert.Equal(["Lost", "Built something else", "Have a look", "Built anyway", "New: PanGloss proposes",
            "Nobody can analyze", "Stopped", "Not parsed"], compare.Presets.Select(preset => preset.Label));
        foreach (var preset in compare.Presets.Where(preset => preset.Label is not ("Have a look" or "Stopped")))
            Assert.All(preset.Cells, cell => Assert.Equal(preset.Label, cell.Label));
        Assert.All(compare.Presets.Single(preset => preset.Label == "Have a look").Cells,
            cell => Assert.Equal(MeaningTone.Look, cell.Tone));
        Assert.All(compare.Presets.Single(preset => preset.Label == "Stopped").Cells,
            cell => Assert.Equal(CompareColumnKind.Timeout, cell.Column));
    }

    [Fact]
    public void EveryCellExplainsItsMeaningInOneLine()
    {
        var compare = new CompareViewModel();

        foreach (var cell in compare.Cells.Where(cell => !cell.IsEmptyImpossible))
        {
            Assert.False(string.IsNullOrWhiteSpace(cell.Explanation), $"{cell.RowLabel} × {cell.ColumnLabel}");
            Assert.DoesNotContain('\n', cell.Explanation!);
            Assert.EndsWith(".", cell.Explanation, StringComparison.Ordinal);
        }
        Assert.Null(Cell(compare, WordProjectStatus.NotPresent, CompareColumnKind.Match).Explanation);
        Assert.Equal("You approved these in FieldWorks; the grammar builds nothing for them.",
            Cell(compare, WordProjectStatus.Approved, CompareColumnKind.NoParse).Explanation);
        Assert.Equal("You disapproved these in FieldWorks; the grammar still builds them.",
            Cell(compare, WordProjectStatus.Rejected, CompareColumnKind.Match).Explanation);
    }

    [Fact]
    public void TheChosenCellsPanelNamesItsCellAndSaysWhatItHoldsInOneLine()
    {
        var compare = LostWords();

        Assert.Equal("All words", compare.ListHeading);
        Assert.Null(compare.ChosenCell);
        Assert.Equal("5 words · 9 places", compare.ListSummary);
        Assert.Equal("Choose a cell to list only its words; Ctrl-click adds cells.", compare.ListExplanation);
        Assert.Equal("AI Handoff for 5 words", compare.HandOffLabel);

        var lost = Cell(compare, WordProjectStatus.Approved, CompareColumnKind.NoParse);
        compare.Toggle(lost, additive: false);

        Assert.Same(lost, compare.ChosenCell);
        Assert.Equal("Approved × No parse", compare.ListHeading);
        Assert.Equal("4 words · 7 places", compare.ListSummary);
        Assert.Equal(lost.Explanation, compare.ListExplanation);
        Assert.Equal("AI Handoff for 4 words", compare.HandOffLabel);

        compare.SearchText = "walikata";
        Assert.Equal("1 of 4 words · 2 places", compare.ListSummary);
        Assert.Equal("AI Handoff for this word", compare.HandOffLabel);

        compare.SearchText = string.Empty;
        compare.Toggle(Cell(compare, WordProjectStatus.Approved, CompareColumnKind.Match), additive: true);
        Assert.Null(compare.ChosenCell);
        Assert.Equal("2 cells chosen", compare.ListHeading);
        Assert.Equal("5 words · 9 places", compare.ListSummary);
    }

    [Fact]
    public void TheChosenCellsWordsShowTheMorphemesTheyShareByIdentity()
    {
        var compare = LostWords();
        Assert.False(compare.HasShared);

        compare.Toggle(Cell(compare, WordProjectStatus.Approved, CompareColumnKind.NoParse), additive: false);

        Assert.True(compare.HasShared);
        Assert.Equal(["-a FV in 4", "wa- 3PL in 2", "kat cut in 2", "ha- NEG in 2", "ja- NEG.PERF in 2"],
            compare.Shared.Select(item => $"{item.Form} {item.Gloss} {item.CountText}"));
        Assert.Equal("kat cut: 2 of these words use it", compare.Shared[2].AccessibleName);

        compare.SearchText = "ha";
        Assert.Equal(["ha- NEG in 2", "ja- NEG.PERF in 2", "-a FV in 2"],
            compare.Shared.Select(item => $"{item.Form} {item.Gloss} {item.CountText}"));

        compare.SearchText = "walikata";
        Assert.False(compare.HasShared);
        Assert.Empty(compare.Shared);
    }

    [Fact]
    public void AStoppedWordCardDoesNotPresentUnreachedApprovedAnalysesAsNotBuilt()
    {
        var a = Approved("a", "kit", "abu");
        var b = Approved("b", "ki", "tabu");
        var source = CardWord(stored: [a, b], built: [a], missed: [b]).Source with { IsIncomplete = true };
        var compare = new CompareViewModel();
        compare.Load([new AssessWordRowViewModel(source)]);

        var word = Assert.Single(compare.Words);

        Assert.Equal(CompareColumnKind.Timeout, word.Column);
        Assert.Empty(word.NotBuiltAnalyses);
        Assert.False(word.ShowsMissedApproved);
    }

    [Fact]
    public void TheCardNamesEveryApprovedAnalysisPanGlossMissed_ExceptTheOneTheRowShows()
    {
        var a = Approved("a", "kit", "abu");
        var b = Approved("b", "ki", "tabu");

        var missesB = CardWord(stored: [a, b], built: [a], missed: [b]);
        Assert.True(missesB.ShowsMissedApproved);
        Assert.Equal(["b"], missesB.NotBuiltAnalyses.Select(reading => reading.StoredAnalysisId));

        var missesTheShownOne = CardWord(stored: [a], built: [], missed: [a]);
        Assert.False(missesTheShownOne.ShowsMissedApproved);
        Assert.Empty(missesTheShownOne.NotBuiltAnalyses);

        var missesBoth = CardWord(stored: [a, b], built: [], missed: [a, b]);
        Assert.True(missesBoth.ShowsMissedApproved);
        Assert.Equal(["b"], missesBoth.NotBuiltAnalyses.Select(reading => reading.StoredAnalysisId));
    }

    internal static ParserReading Approved(string id, params string[] forms) =>
        new([.. forms.Select(form => new ParserReadingMorph(form, form + "-gloss", "n", null, false, null)
        {
            AllomorphId = IdOf("allomorph " + form),
            GrammaticalInfoId = IdOf("grammatical info " + form),
        })])
        {
            StoredAnalysisId = id,
            StoredAnalysisOpinion = ReadingGrade.Approved,
        };

    // A word FieldWorks approved as each of stored, in order, for which PanGloss built built and missed missed.
    internal static CompareWordViewModel CardWord(ParserReading[] stored, ParserReading[] built, ParserReading[] missed)
    {
        var compare = new CompareViewModel();
        compare.Load([new AssessWordRowViewModel(new AssessmentWordResult(
            "kitabu", built.Length > 0 ? "analysed" : "no-analysis", false, "Search completed", 10, null)
        {
            ProjectStanding = ProjectStanding.Approved,
            ExpectedAnalysis = stored[0],
            StoredAnalyses = stored,
            Readings = built,
            ReadingGrades = [.. built.Select(_ => ReadingGrade.Approved)],
            MissedApproved = missed,
            Morphology = new ParseWordEvidence("v1", 0, "kitabu", 10, false, false, false,
                [.. built.Select(_ => new ParseAnalysis([]))], []),
        })]);
        return compare.Words.Single();
    }

    [Fact]
    public void TheMeaningColumnShowsOnlyWhenTheListedCellsMixMeanings()
    {
        var compare = LostWords(
            new AssessmentWordResult("polepole", "timed-out", true, "Search stopped at its time limit", 10, null)
            {
                ProjectStanding = ProjectStanding.NotPresent,
            },
            new AssessmentWordResult("haraka", "timed-out", true, "Search stopped at its time limit", 10, null)
            {
                ProjectStanding = ProjectStanding.Approved,
            });
        Assert.True(compare.ShowsMeaning);
        Assert.Null(compare.ListMeaning);

        var lost = Cell(compare, WordProjectStatus.Approved, CompareColumnKind.NoParse);
        compare.Toggle(lost, additive: false);
        Assert.False(compare.ShowsMeaning);
        Assert.Null(compare.ListMeaning);

        compare.Toggle(Cell(compare, WordProjectStatus.Approved, CompareColumnKind.Match), additive: true);
        Assert.True(compare.ShowsMeaning);

        compare.SelectPresetCommand.Execute(compare.Presets.Single(preset => preset.Label == "Stopped"));
        Assert.False(compare.ShowsMeaning);
        Assert.Equal("5 cells chosen", compare.ListHeading);
        Assert.Equal("No result", compare.ListMeaning);
        Assert.Equal("neutral", compare.ListMeaningMark?.Value);

        compare.ClearSelectionCommand.Execute(null);
        Assert.True(compare.ShowsMeaning);
    }

    [Fact]
    public void TheStripShowsTheEightMostSharedMorphemesAndCountsTheRest()
    {
        var morphs = Enumerable.Range(1, 11).Select(index => IdMorph($"m{index}-", $"G{index}")).ToArray();
        var table = new AssessWordsViewModel();
        table.Load([Lost("one", 1, morphs), Lost("two", 1, morphs)]);
        var compare = new CompareViewModel();
        compare.Load(table.AllRows);

        compare.Toggle(Cell(compare, WordProjectStatus.Approved, CompareColumnKind.NoParse), additive: false);

        Assert.Equal(Enumerable.Range(1, 8).Select(index => $"m{index}-"), compare.Shared.Select(item => item.Form));
        Assert.Equal("and 3 more", compare.SharedMoreText);
        compare.SearchText = "one";
        Assert.Null(compare.SharedMoreText);
    }

    // Four approved words PanGloss could not parse, and one it kept that shares nothing with them by identity.
    internal static CompareViewModel LostWords(params AssessmentWordResult[] more)
    {
        var table = new AssessWordsViewModel();
        table.Load([
            .. more,
            Lost("walikata", 2, IdMorph("wa-", "3PL"), IdMorph("li-", "PST"), IdMorph("kat", "cut"), IdMorph("-a", "FV")),
            Lost("anakata", 1, IdMorph("a-", "3SG"), IdMorph("na-", "PRS"), IdMorph("kat", "cut"), IdMorph("-a", "FV")),
            Lost("hawajafika", 3, IdMorph("ha-", "NEG"), IdMorph("wa-", "3PL"), IdMorph("ja-", "NEG.PERF"),
                IdMorph("fik", "arrive"), IdMorph("-a", "FV")),
            Lost("hatujaona", 1, IdMorph("ha-", "NEG"), IdMorph("tu-", "1PL"), IdMorph("ja-", "NEG.PERF"),
                IdMorph("on", "see"), IdMorph("-a", "FV")),
            Word("kata", "analysed", ProjectStanding.Approved, ["approved"], occurrences: 2),
        ]);
        var compare = new CompareViewModel();
        compare.Load(table.AllRows);
        return compare;
    }

    private static AssessmentWordResult Lost(string word, int places, params ParserReadingMorph[] morphs) =>
        new(word, "no-analysis", false, "Search completed", 10, null)
        {
            ProjectStanding = ProjectStanding.Approved,
            OccurrenceCount = places,
            StoredAnalyses = [new ParserReading(morphs)
            {
                StoredAnalysisId = "stored-" + word,
                StoredAnalysisOpinion = ReadingGrade.Approved,
            }],
        };

    private static ParserReadingMorph IdMorph(string form, string gloss) => new(form, gloss, "v", null, false, null)
    {
        AllomorphId = IdOf("allomorph " + form + gloss),
        GrammaticalInfoId = IdOf("grammatical info " + gloss),
    };

    private static string IdOf(string text) =>
        new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToString("D");

    private static CompareCellViewModel Cell(CompareViewModel compare, WordProjectStatus row, CompareColumnKind column) =>
        compare.Cells.Single(cell => cell.Row == row && cell.Column == column);

    [Fact]
    public void FixFirstKeepsTheSharedReasonWhenApprovedAndDisapprovedAnalysesWereBothBuilt()
    {
        var approved = StoredFor("shared", ReadingGrade.Approved) with { StoredAnalysisId = "approved" };
        var disapproved = approved with
        {
            StoredAnalysisId = "disapproved", StoredAnalysisOpinion = ReadingGrade.Disapproved,
        };
        var word = new AssessmentWordResult("mixed", "analysed", false, "Search completed", 10, null)
        {
            ProjectStanding = ProjectStanding.Approved,
            StoredAnalyses = [approved, disapproved],
            ReadingGrades = [ReadingGrade.Approved],
            Readings = [approved],
            Morphology = new ParseWordEvidence("v1", 0, "mixed", 10, false, false, false,
                [new ParseAnalysis([new ParseMorph("shared", "n", null, null)])], []),
        };
        word = word with { FixFirst = CompareSemantics.FixFirst(CompareWordFacts.Of(word), []) };
        var compare = new CompareViewModel();
        compare.Load([new AssessWordRowViewModel(word)]);

        var row = Assert.Single(compare.FixFirstRows);
        Assert.Equal(CompareColumnKind.NoMatch, row.Word.Column);
        Assert.Empty(row.MissedApproved);
        Assert.Equal("Built something else", row.Word.Meaning);
        Assert.Equal(row.Word.Meaning, row.Word.WordRow.Meaning);
        Assert.Equal("Rebuilt an analysis you Disapproved", row.Priority.Explanation);
        Assert.Equal(row.Priority.Explanation, row.Explanation);
    }

    [Fact]
    public void FixFirstRanksTheNamedProblemsByFrequencyAndLeavesUnknownOut()
    {
        var table = new AssessWordsViewModel();
        table.Load([
            Word("approved-common", "no-analysis", ProjectStanding.Approved, missedApproved: 1, occurrences: 9),
            Word("approved-rare", "no-analysis", ProjectStanding.Approved, missedApproved: 1, occurrences: 2),
            Word("approved-different", "analysed", ProjectStanding.Approved, ["no-opinion"], missedApproved: 1, occurrences: 30),
            Word("rejected-rebuilt", "analysed", ProjectStanding.Rejected, ["disapproved"], occurrences: 7),
            Word("candidate-unbuilt", "no-analysis", ProjectStanding.Candidate, occurrences: 6),
            Word("timed-out", "timed-out", ProjectStanding.Approved, incomplete: true, occurrences: 100),
            Word("skipped", "skipped", ProjectStanding.Candidate, occurrences: 200),
        ]);
        var compare = new CompareViewModel();
        compare.Load(table.AllRows);

        Assert.Equal(
            ["approved-common", "approved-rare", "approved-different", "rejected-rebuilt", "candidate-unbuilt"],
            compare.FixFirstRows.Select(item => item.Word.Word));
        Assert.Equal("Expected form missed, not built.", compare.FixFirstRows[0].Explanation);

        compare.FocusFixFirstCommand.Execute(compare.FixFirstRows[2]);

        Assert.Equal(["approved-different"], compare.Words.Select(word => word.Word));
        Assert.Equal((WordProjectStatus.Approved, CompareColumnKind.NoMatch),
            (compare.Cells.Single(cell => cell.IsSelected).Row, compare.Cells.Single(cell => cell.IsSelected).Column));
    }

    [Fact]
    public void FocusingAFixFirstWordMatchesTheExactForm()
    {
        var table = new AssessWordsViewModel();
        table.Load([
            Word("at", "no-analysis", ProjectStanding.Approved, missedApproved: 1),
            Word("mo'at", "no-analysis", ProjectStanding.Approved, missedApproved: 1),
        ]);
        var compare = new CompareViewModel();
        compare.Load(table.AllRows);

        compare.FocusFixFirstCommand.Execute(compare.FixFirstRows.Single(row => row.Word.Word == "at"));

        Assert.Equal(["at"], compare.Words.Select(word => word.Word));
    }

    [Fact]
    public void MatrixOffersOnlyChangesAllowedForBulkSelection()
    {
        var (_, compare) = Loaded();
        var word = compare.Words.Single(item => item.Word == "kitabu");
        word.IsChecked = true;

        Assert.Equal("1 word ticked", compare.CheckedWordText);
        Assert.True(compare.HasCheckedWords);
        Assert.False(compare.ProposeCommand.CanExecute(ChangeKinds.Approve));
        Assert.False(compare.ProposeCommand.CanExecute(ChangeKinds.Reject));
        Assert.False(compare.ProposeCommand.CanExecute(ChangeKinds.Candidate));
        Assert.True(compare.ProposeCommand.CanExecute(ChangeKinds.AddCandidate));
        Assert.True(compare.ProposeCommand.CanExecute(ChangeKinds.IncorrectSpelling));
    }

    [Fact]
    public void AStaleWordTakesPriorityInItsCellPendingStatus()
    {
        var words = new AssessWordsViewModel();
        words.Load([
            Word("first", "no-analysis", ProjectStanding.Approved, missedApproved: 1),
            Word("second", "no-analysis", ProjectStanding.Approved, missedApproved: 1),
        ]);
        var compare = new CompareViewModel();
        compare.Load(words.AllRows);
        var changes = new ChangesViewModel(new FakeCommandClient());
        compare.Changes = changes;
        changes.Items.Add(new ChangeViewModel(ChangeKinds.IncorrectSpelling, "first", ""));
        changes.Items.Add(new ChangeViewModel(ChangeKinds.IncorrectSpelling, "second", "", "stale",
            new SIL.Motif.Contract.Responses.ChangeFit("stale", false, ["The project changed."])));

        Assert.Equal("No longer fits", compare.Cells.Single(cell =>
            cell.Row == WordProjectStatus.Approved && cell.Column == CompareColumnKind.NoParse).PendingChangeStatus);
    }

    [Fact]
    public void PendingChangesMarkTheirCellsAndSurfaceWhenTheyNoLongerFit()
    {
        var (_, compare) = Loaded();
        var changes = new ChangesViewModel(new FakeCommandClient());
        compare.Changes = changes;
        var word = compare.Words.Single(item => item.Word == "kitabu");

        changes.Items.Add(new ChangeViewModel(ChangeKinds.IncorrectSpelling, word.Word, "", "change-1",
            new SIL.Motif.Contract.Responses.ChangeFit("change-1", false, ["The project changed."])));

        Assert.True(word.HasPendingChange);
        Assert.Equal("No longer fits", word.PendingChangeStatus);
        Assert.Equal(PendingChangeState.NoLongerFits, word.PendingState);
        Assert.Equal("No longer fits", compare.Cells.Single(cell => cell.Row == word.Row && cell.Column == word.Column).PendingChangeStatus);
        Assert.Equal(PendingChangeState.NoLongerFits,
            compare.Cells.Single(cell => cell.Row == word.Row && cell.Column == word.Column).PendingState);
    }
}
