using System.Text.Json;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that a word's row reads the same on every page that reaches it, in the window's marks and words, with the
/// three next steps always present.
/// </summary>
public sealed class WordRowViewModelTests
{
    private const string Digest = "sha256:" + "0000000000000000000000000000000000000000000000000000000000000000";

    private static (AssessWordsViewModel Words, CompareViewModel Compare, TextsListsViewModel Lists) Loaded(
        IEnumerable<AssessmentWordResult> rows)
    {
        var words = new AssessWordsViewModel();
        words.Load(rows.ToArray());
        var compare = new CompareViewModel();
        compare.Load(words.AllRows);
        return (words, compare, new TextsListsViewModel(compare));
    }

    private static string Fields(WordRowViewModel row) => JsonSerializer.Serialize(row.Row);

    [Fact]
    public void EachWordShowsItsOwnMeasurementTime()
    {
        var first = DateTimeOffset.Parse("2026-09-24T11:00:00Z");
        var second = first.AddMinutes(5);
        var rows = new[] { first, second }.Select((stamp, index) => new WordRowViewModel(new WordRow(
            "word" + index, WordRowOutcome.Same, "Kept", WordRowTone.Fine)
        { Origin = new WordMeasurementOrigin("run" + index, "invocation" + index, stamp) })).ToArray();

        Assert.Contains(first.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture), rows[0].MeasuredText);
        Assert.Contains(second.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture), rows[1].MeasuredText);
        Assert.NotEqual(rows[0].MeasuredText, rows[1].MeasuredText);
    }

    [Fact]
    public void OneWordYieldsIdenticalRowFieldsFromTheMatrixListsAndTiming()
    {
        var (words, compare, lists) = Loaded(MatrixListsWindowWordsTests.EveryKindOfWord);
        var reachedFromLists = 0;

        foreach (var word in MatrixListsWindowWordsTests.EveryKindOfWord)
        {
            var timing = words.Find(word.Word)!.WordRow;
            var (cellRow, cellColumn) = CompareViewModel.Place(words.Find(word.Word)!);
            compare.SelectCells([new TextsListCell(cellRow, cellColumn)]);
            var placed = compare.Words.Single(row => row.Word == word.Word);
            var matrix = placed.WordRow;

            Assert.Equal(Fields(timing), Fields(matrix));
            Assert.Equal(WindowWords.OutcomeOf(placed.Column), matrix.Outcome);
            Assert.Equal(placed.Meaning, matrix.Meaning);
            Assert.Equal(placed.Tone, matrix.Tone);

            var list = lists.Lists.FirstOrDefault(candidate =>
                candidate.Cells.Contains(new TextsListCell(placed.Row, placed.Column)));
            if (list is null) continue;
            lists.SelectListCommand.Execute(list);
            var listed = compare.Words.Single(row => row.Word == word.Word).WordRow;
            Assert.Equal(Fields(timing), Fields(listed));
            reachedFromLists++;
        }

        Assert.True(reachedFromLists >= 5, $"Only {reachedFromLists} words were reachable from Lists.");
    }

    public static IEnumerable<object[]> EveryStandingAndColumn() =>
        from standing in new[]
        {
            ProjectStanding.Approved, ProjectStanding.Candidate, ProjectStanding.Rejected,
            ProjectStanding.IncorrectSpelling, ProjectStanding.NotPresent,
        }
        from column in Enum.GetValues<CompareColumnKind>()
        select new object[] { standing, column };

    [Theory]
    [MemberData(nameof(EveryStandingAndColumn))]
    public void TheRowsOutcomeMeaningAndToneAreTheMatrixs(string standing, CompareColumnKind column)
    {
        var row = new WordRowViewModel(WordRowProjection.Of(
            new AssessmentWordResult("w", column switch
            {
                CompareColumnKind.Skipped => "unassessed",
                CompareColumnKind.NoParse => "no-analysis",
                _ => "analysed",
            }, column == CompareColumnKind.Timeout, "Search completed", 1, null)
            {
                ProjectStanding = standing,
                ReadingGrades = [column == CompareColumnKind.Match ? standing switch
                {
                    ProjectStanding.Approved => ReadingGrade.Approved,
                    ProjectStanding.Candidate => ReadingGrade.Candidate,
                    ProjectStanding.Rejected => ReadingGrade.Disapproved,
                    _ => ReadingGrade.Approved,
                } : ReadingGrade.NoOpinion],
                Morphology = new ParseWordEvidence("v1", 0, "w", 1, false, false, false,
                    [new ParseAnalysis([new ParseMorph("form", "msa", null, null)])], []),
            }));
        var (word, tone) = WindowWords.MeaningOf(standing, WindowWords.OutcomeOf(column));

        Assert.Equal(WindowWords.OutcomeOf(column), row.Outcome);
        Assert.Equal((word, tone), (row.Meaning, row.Tone));
        Assert.Equal(Mark.Of(tone), row.MeaningMark);
        Assert.Equal(Mark.Of(row.Outcome), row.OutcomeMark);
    }

    private static readonly ParserReadingMorph Kul = new("kul", "cut", "v", null, false, null)
        { AllomorphId = "form-kul", GrammaticalInfoId = "msa-kul" };

    private static WordRow Alikula() => new("alikula", WordRowOutcome.Different, "Built something else", WordRowTone.Problem)
    {
        Gloss = "3SG PST cut FV",
        Opinion = ProjectStanding.Approved,
        FieldWorksMorphemes = [Kul],
        PanGlossMorphemes =
        [
            new("ku-", "INF", "v", null, false, null), new("l", "eat", "v", null, false, null), Kul,
        ],
        DifferingPositions = [1, 2],
        PanGlossReadingCount = 2,
        Places = 3,
        ElapsedMs = 12,
        WordAnalysesLink = "silfw://localhost/link?database%3dp%26tool%3dAnalyses",
    };

    [Fact]
    public void TheRowShowsTheWindowsMarksAndWords()
    {
        var row = new WordRowViewModel(Alikula());

        Assert.Equal(("alikula", "3SG PST cut FV"), (row.Word, row.Gloss));
        Assert.Equal(Mark.Approved, row.OpinionMark);
        Assert.Equal("Approved", row.OpinionLabel);
        Assert.Equal((Mark.Different, "Different"), (row.OutcomeMark, row.OutcomeWord));
        Assert.Equal(["kul"], row.FieldWorksMorphemes.Select(morph => morph.Form));
        Assert.Equal([("ku-", true), ("l", true), ("kul", false)],
            row.PanGlossMorphemes.Select(morph => (morph.Morph.Form, morph.IsDifferent)));
        Assert.True(row.HasPanGlossMorphemes);
        Assert.Equal("×3", row.PlacesText);
        Assert.Equal("12 ms", row.ElapsedText);
        Assert.False(row.HasWarningsNamed);
        Assert.Equal("alikula · Approved · PanGloss: Different · Built something else", row.Summary);
    }

    [Fact]
    public void TheFieldWorksMorphemesKeepTheirIds()
    {
        var morph = Assert.Single(new WordRowViewModel(Alikula()).FieldWorksMorphemes);

        Assert.Equal(("form-kul", "msa-kul"), (morph.AllomorphId, morph.GrammaticalInfoId));
    }

    [Fact]
    public void TheThreeNextStepsAreAlwaysThere_AndOpenTheWordWhereEachSays()
    {
        var opened = new List<string>();
        var tried = new List<string>();
        var routes = new WordRowRoutes { OpenInText = opened.Add, TryWord = tried.Add };
        var row = new WordRowViewModel(Alikula(), routes);

        Assert.Equal(("Open in text", "Try a Word", "Word Analyses ↗"),
            (row.OpenInTextLabel, row.TryWordLabel, row.WordAnalysesLabel));
        Assert.Equal("Open alikula in Word Analyses", row.WordAnalysesName);
        Assert.Equal(new Uri("silfw://localhost/link?database%3dp%26tool%3dAnalyses"), row.WordAnalysesLink);

        row.OpenInTextCommand.Execute(null);
        row.TryWordCommand.Execute(null);

        Assert.Equal(["alikula"], opened);
        Assert.Equal(["alikula"], tried);
    }

    [Fact]
    public void AWordWithoutTextsOpensInTheWordListAndUpdatesItsAccessibleName()
    {
        var routes = new WordRowRoutes { OpenInText = _ => { } };
        var row = new WordRowViewModel(Alikula(), routes);

        Assert.Equal("Open alikula in Analyze texts", row.OpenInTextAutomationName);

        routes.HasTexts = false;

        Assert.Equal("Open word", row.OpenInTextLabel);
        Assert.Equal("Open alikula", row.OpenInTextAutomationName);
    }

    [Fact]
    public void AWordFieldWorksDoesNotHoldHasNoWordAnalysesLink_AndSaysSo()
    {
        var row = new WordRowViewModel(Alikula() with { WordAnalysesLink = null });

        Assert.False(row.HasWordAnalysesLink);
        Assert.Equal("FieldWorks has no wordform spelled alikula", row.WordAnalysesDisabledReason);
    }

    [Fact]
    public void ADisapprovedMatchKeepsTheParserOutcomeSeparateFromItsMeaning()
    {
        var row = new WordRowViewModel(new WordRow("word", WordRowOutcome.Same, "Built anyway", WordRowTone.Problem)
        {
            Opinion = ProjectStanding.Rejected,
        });

        Assert.Equal("Built anyway", row.Meaning);
        Assert.Equal("Same", row.CompactOutcomeWord);
        Assert.Equal(MarkKind.Outcome, row.CompactOutcomeMark.Kind);
    }

    [Fact]
    public void AnUnparsedWordUsesItsOnlyFieldWorksGlossAsItsMeaning()
    {
        var row = WordRowViewModel.NotParsed("kitabu", ProjectStanding.Approved,
            [new ParserReadingMorph("kitabu", "book", "n", null, false, null)]);

        Assert.Equal("book", row.Meaning);
        Assert.Equal("Not parsed", row.OutcomeWord);
    }

    [Fact]
    public void AnUnreadWordSaysSo_AndAReadOneShowsNothing()
    {
        var (words, _, _) = Loaded(MatrixListsWindowWordsTests.EveryKindOfWord);
        var row = words.Find("approved-kept")!.WordRow;
        Assert.Null(row.IsUnread);
        Assert.False(row.ShowUnread);

        words.ApplyReadState(word => word == "approved-kept");

        Assert.True(row.ShowUnread);
        Assert.Equal("Unread", row.UnreadText);
        Assert.True(row.Row.IsUnread);
        Assert.False(words.Find("approved-empty")!.WordRow.ShowUnread);
    }

    [Fact]
    public void TheReadMarksAnalyzeTextsLoadsReachTheWordRows()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            const string projectPath = "word-row.fwdata";
            var client = new FakeCommandClient();
            var selection = new SelectionViewModel(client) { AllWordforms = true };
            var texts = new TextWordsViewModel(client, selection, client.ReaderOwner);
            var assess = new AssessViewModel(client, selection) { ProjectPath = projectPath };
            var changes = new ChangesViewModel(client);
            await changes.OpenProjectAsync(projectPath);
            var line = new TextLine(1, [new TextToken("kata", "kata", null, "unanalysed")
            {
                WordformId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
                OccurrenceIndex = 0,
            }])
            {
                ParagraphId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
                SegmentId = Guid.Parse("cccccccc-0000-0000-0000-000000000001"),
                ParseIsCurrent = true,
            };
            var source = new TextWordsResponse([],
                [new TextLines(Guid.Parse("dddddddd-0000-0000-0000-000000000001"), "Text", [line])], true);
            await using var fixture = new SelectionModelFixture(client);
            var inText = new ResultsInTextViewModel(texts, assess, _ => { }, _ => { }, changes, client, client.ReaderOwner);
            await texts.SetProjectAsync(projectPath);
            client.AssessCompletesWith(SelectionModelFixture.WithOrigins(new AssessCommandResponse(
                new BaselineCaptureResponse(new BaselineToken("p", Digest, "1", "2026-09-05T00:00:00Z", Digest),
                    projectPath, DateTimeOffset.UtcNow, FieldWorksHeldProject: false, ReusedExistingBytes: true),
                new SelectionProjection([], []), ["assessment/row"], "(summary)")
            {
                Measurements = [new ProducedAssessmentReference("assessment/row", AssessmentKinds.ParseTime, "invocation/row")],
                Words =
                [
                    new AssessmentWordResult("kata", "no-analysis", false, "Complete", 3, null),
                    new AssessmentWordResult("elsewhere", "no-analysis", false, "Complete", 3, null),
                ],
            }));

            await assess.RunCommand.ExecuteAsync(null);
            await fixture.PublishAsync(source, assess.Result);
            await SelectionModelFixture.RealizeAsync(inText);

            Assert.True(assess.Words.Find("kata")!.WordRow.ShowUnread);
            Assert.Null(assess.Words.Find("elsewhere")!.WordRow.IsUnread);
            client.OnReadWordState((request, _) => Task.FromResult(fixture.WriteReadState(request)));
            await inText.MarkTextReadCommand.ExecuteAsync(null);
            Assert.False(assess.Words.Find("kata")!.WordRow.ShowUnread);
            Assert.False(assess.Words.Find("kata")!.WordRow.IsUnread);
            Assert.Null(assess.Words.Find("elsewhere")!.WordRow.IsUnread);
            await inText.MarkTextUnreadCommand.ExecuteAsync(null);
            Assert.True(assess.Words.Find("kata")!.WordRow.ShowUnread);
            await inText.StopAsync();
            await texts.StopAsync();
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void TheRoutesTheWordsListHoldsReachEveryRow()
    {
        var (words, compare, _) = Loaded(MatrixListsWindowWordsTests.EveryKindOfWord);
        var tried = new List<string>();
        words.Routes.TryWord = tried.Add;
        var (cellRow, cellColumn) = CompareViewModel.Place(words.Find("nobody")!);
        compare.SelectCells([new TextsListCell(cellRow, cellColumn)]);

        compare.Words.Single(row => row.Word == "nobody").WordRow.TryWordCommand.Execute(null);

        Assert.Equal(["nobody"], tried);
    }
}
