using System.Linq;
using System.Globalization;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins <see cref="TextWordsViewModel"/>: it reloads whenever the checked Texts change, computes each
/// word's project status (None, Approved, Several analyses), the toolbar's summary counts, and feeds
/// <see cref="TextChoiceViewModel"/>'s own counts.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextWordsViewModelTests : IAsyncLifetime
{
    private readonly Dictionary<FakeCommandClient, (SelectionModelFixture Fixture, TextWordsViewModel Words)> _fixtures = [];
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            foreach (var item in _fixtures.Values)
            {
                await item.Words.StopAsync();
                await item.Fixture.DisposeAsync();
            }
        }, TimeSpan.FromSeconds(60));
        return Task.CompletedTask;
    }

    private async Task PublishAsync(FakeCommandClient client, TextWordsResponse source)
    {
        var item = _fixtures[client];
        await item.Fixture.PublishAsync(SelectionModelFixture.WithOccurrenceTexts(source));
        if (item.Words.Rows.Count > 20) return;
        foreach (var row in item.Words.Rows) item.Words.RealizeRow(row);
        await item.Words.SettleVisibleDetailsAsync();
    }

    private async Task ShowAssessmentAsync(FakeCommandClient client, AssessWordsViewModel assessed)
    {
        var item = _fixtures[client];
        var token = new BaselineToken("fixture-project", "sha256:" + new string('a', 64), "1",
            "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));
        var response = SelectionModelFixture.WithOrigins(new AssessCommandResponse(
            new BaselineCaptureResponse(token, ProjectPath, DateTimeOffset.UnixEpoch, false, true),
            new SelectionProjection([], []), ["assessment/words"], "fixture")
        {
            InvocationId = "invocation/words",
            Measurements = [new ProducedAssessmentReference("assessment/words", AssessmentKinds.ParseTime,
                "invocation/words")],
            Words = assessed.Sources,
        });
        await item.Fixture.PublishAsync(item.Fixture.Source!, response);
        foreach (var row in item.Words.Rows) item.Words.RealizeRow(row);
        await item.Words.SettleVisibleDetailsAsync();
        assessed.Load(response.Words);
        item.Words.ShowAssessment(assessed.Find);
    }

    private const string ProjectPath = @"C:\projects\one.fwdata";
    private static readonly Guid TextId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ProjectAnalysis Analysis(string key, string gloss) =>
        new(key, [new ParserReadingMorph("kitabu", gloss, "n", null, false, null)]);

    [Fact]
    public void LargeWordListsDelayOccurrenceProjectionsUntilARowIsRead()
    {
        var analysis = Analysis("book", "book");
        var occurrences = Enumerable.Range(0, 500).Select(index =>
            new WordOccurrence(TextId, "Story", index + 1, "kitabu", "approved", analysis)).ToArray();
        var word = new TextWord("kitabu", Guid.NewGuid().ToString(), occurrences, [analysis], [])
            { Analyses = [analysis] };
        _ = new TextWordRowViewModel(word);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var rows = Enumerable.Range(0, 2000).Select(_ => new TextWordRowViewModel(word)).ToArray();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 32 * 1024 * 1024, $"Creating the list allocated {allocated:N0} bytes before a row was read.");
        Assert.All(rows, row => Assert.Equal(500, row.OccurrenceCount));
        Assert.Equal(500, rows[0].Occurrences.Count);
        Assert.Equal("book", Assert.Single(rows[0].ApprovedAnalyses).Gloss);
    }

    [Fact]
    public void UnreadWordListRowsDoNotBuildTheirStoredMorphologySummaries()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, _, words) = NewViewModel();
            await words.SetProjectAsync(ProjectPath);
            var morphs = Enumerable.Range(0, 32).Select(index =>
                new ParserReadingMorph($"morph-{index}", $"meaning-{index}", "n", null, false, null)).ToArray();
            var analysis = new ProjectAnalysis("stored", morphs);
            var occurrences = new[] { new WordOccurrence(TextId, "Story", 1, "word", "approved", analysis) };
            var source = Enumerable.Range(0, 21604).Select(index => new TextWord($"word-{index}", null,
                occurrences, [analysis], []) { Analyses = [analysis] }).ToArray();
            await PublishAsync(fake, new TextWordsResponse(source, [], true, 21604));
            var before = GC.GetAllocatedBytesForCurrentThread();

            _ = words.Rows.Count;
            _ = words.ApprovedFilterCount;
            words.SearchText = "word-21603";

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(allocated < 16 * 1048576L, $"The unread list allocated {allocated / 1048576d:F1} MiB");
            Assert.Equal(0, words.MaterializedRowCount);
            var final = Assert.Single(words.Rows);
            Assert.Equal(1, words.MaterializedRowCount);
            Assert.Equal("word-21603", final.Form);
            words.RealizeRow(final);
            await words.SettleVisibleDetailsAsync();
            Assert.Contains("meaning-31", final.ProjectSummary);
        }, TimeSpan.FromSeconds(180));
    }

    private (FakeCommandClient Fake, SelectionViewModel Selection, TextWordsViewModel Words) NewViewModel()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var words = new TextWordsViewModel(fake, selection, fake.ReaderOwner);
        _fixtures.Add(fake, (new SelectionModelFixture(fake), words));
        return (fake, selection, words);
    }

    [Fact]
    public void ReloadingCountsLargeTextsWithoutMaterializingEveryOccurrenceRow()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, selection, words) = NewViewModel();
            fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Story")], HasBaseline: true));
            await selection.SetProjectAsync(ProjectPath);
            await words.SetProjectAsync(ProjectPath);
            selection.Texts[0].IsChecked = true;
            var analysis = Analysis("book", "book");
            var occurrences = Enumerable.Range(0, 10).Select(index =>
                new WordOccurrence(TextId, "Story", index + 1, "kitabu", "approved", analysis)).ToArray();
            var source = Enumerable.Range(0, 2000).Select(index => new TextWord($"word-{index}", null,
                occurrences, [analysis], []) { Analyses = [analysis] }).ToArray();
            await PublishAsync(fake, new TextWordsResponse(source, [], true, OccurrenceCount: 20_000));
            var before = GC.GetAllocatedBytesForCurrentThread();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(allocated < 32 * 1024 * 1024, $"Reloading allocated {allocated:N0} bytes before a row was read.");
            Assert.Equal(2000, words.Rows.Count);
            Assert.Equal($"{20_000:N0} words · {2_000:N0} distinct", selection.Texts[0].CountsText);
            var first = words.Rows[0];
            first.Listed.IsOpen = true;
            await words.CardPending;
            Assert.Equal(10, first.Occurrences.Count);
        }, TimeSpan.FromSeconds(180));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoredAssessmentKeepsEachVisibleHomographsOwnProjectFacts(bool reverse)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var firstId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011");
            var otherId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000099");
            var approved = Analysis("book", "book") with
                { StoredAnalysisId = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(Guid.Parse("dddddddd-0000-0000-0000-000000000001")).Value, StoredAnalysisOpinion = ReadingGrade.Approved };
            var rejected = Analysis("love", "love") with
                { StoredAnalysisId = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(Guid.Parse("dddddddd-0000-0000-0000-000000000002")).Value, StoredAnalysisOpinion = ReadingGrade.Disapproved };
            var first = new TextWord("kitabu", firstId.ToString(),
                [new WordOccurrence(TextId, "Alpha", 1, "kitabu", "approved", approved)], [approved], [])
                { Analyses = [approved] };
            var other = new TextWord("kitabu", otherId.ToString(),
                [new WordOccurrence(TextId, "Alpha", 2, "kitabu", "disapproved", rejected)], [], [rejected])
                { Analyses = [rejected] };
            var (fake, _, words) = NewViewModel();
            await words.SetProjectAsync(ProjectPath);
            await PublishAsync(fake, new TextWordsResponse(reverse ? [other, first] : [first, other], [], true));
            var own = words.Rows.Single(row => row.WordformId == firstId);
            var homograph = words.Rows.Single(row => row.WordformId == otherId);
            Assert.Equal("book", own.Listed.Row.Gloss);
            Assert.Equal("love", homograph.Listed.Row.Gloss);
            var assessed = new AssessWordsViewModel();
            assessed.Load([new AssessmentWordResult("kitabu", "analysed", false, "Complete", 4, null)
            {
                ProjectStanding = ProjectStanding.Approved,
                Comparison = new WordComparison(ProjectStanding.Approved, WordRowOutcome.Same,
                    "kept", "Shared spelling comparison", WordRowTone.Fine),
                StoredAnalyses = [new ParserReading(approved.Morphs)
                    { StoredAnalysisId = approved.StoredAnalysisId, StoredAnalysisOpinion = ReadingGrade.Approved }],
                ExpectedAnalysis = new ParserReading(approved.Morphs),
                ReadingGrades = [ReadingGrade.Approved],
                Readings = [new ParserReading(approved.Morphs)],
            }]);

            await ShowAssessmentAsync(fake, assessed);

            own = words.Rows.Single(row => row.WordformId == firstId);
            homograph = words.Rows.Single(row => row.WordformId == otherId);
            Assert.Equal(Mark.Approved, own.Listed.Row.OpinionMark);
            Assert.Equal(Mark.Disapproved, homograph.Listed.Row.OpinionMark);
            Assert.Equal("book", own.Listed.Row.Gloss);
            Assert.Equal("love", homograph.Listed.Row.Gloss);
            Assert.Null(homograph.Listed.Card);
            homograph.Listed.IsOpen = true;
            Assert.Equal(rejected.StoredAnalysisId, Assert.Single(homograph.Listed.Card!.Marking.FieldWorksAnalyses).StoredAnalysisId);
            Assert.Equal(ReadingGrade.Disapproved,
                Assert.Single(homograph.Listed.Card.Marking.FieldWorksAnalyses).Opinion);
            Assert.NotEqual(ParserOutcome.Same, homograph.Listed.Row.Outcome);
            Assert.Equal("×1", homograph.Listed.Row.PlacesText);
            Assert.Equal(Mark.Disapproved, homograph.Listed.Card!.WordRow.OpinionMark);
            Assert.Equal("love", homograph.Listed.Card.WordRow.Gloss);
            Assert.NotEqual(assessed.Find("kitabu")!.Comparison, homograph.Listed.Card.Source.Comparison);
            words.ShowAssessment(null);
            Assert.Equal("love", homograph.Listed.Row.Gloss);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void WordsAreListedMostFrequentFirstWithTheLatestAssessmentsResult()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, _, words) = NewViewModel();
            await words.SetProjectAsync(ProjectPath);
            await PublishAsync(fake, new TextWordsResponse(
                [new TextWord("mara", null, [new WordOccurrence(TextId, "Alpha", 1, "s", "unanalysed", null)], [], []),
                 new TextWord("na", null,
                    [new WordOccurrence(TextId, "Alpha", 2, "s", "unanalysed", null),
                     new WordOccurrence(TextId, "Alpha", 3, "s", "unanalysed", null)], [], [])],
                [], HasBaseline: true, OccurrenceCount: 3));
            var assessed = new AssessWordsViewModel();
            assessed.Load([new AssessmentWordResult("na", "no-analysis", false, "Search completed", 4, null)]);

            await ShowAssessmentAsync(fake, assessed);

            Assert.Equal(["na", "mara"], words.Rows.Select(row => row.Form));
            Assert.Equal("No parse", words.Rows[0].LastResultLabel);
            Assert.False(words.Rows[1].HasLastResult);
            Assert.Equal("Not present", words.Rows[1].StatusLabel);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void EachWordsRowIsTheParsedRowOnceAParseReachesIt_AndWhatFieldWorksHoldsUntilThen()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, _, words) = NewViewModel();
            var tried = new List<string>();
            words.WordRowRoutes = new WordRowRoutes { TryWord = tried.Add };
            await words.SetProjectAsync(ProjectPath);
            await PublishAsync(fake, new TextWordsResponse(
                [new TextWord("kitabu", null,
                    [new WordOccurrence(TextId, "Alpha", 1, "kitabu.", "approved", Analysis("k1", "book")),
                     new WordOccurrence(TextId, "Alpha", 2, "kitabu.", "approved", Analysis("k1", "book"))],
                    [Analysis("k1", "book")], []),
                 new TextWord("na", null, [new WordOccurrence(TextId, "Alpha", 3, "s", "unanalysed", null)], [], [])],
                [], HasBaseline: true, OccurrenceCount: 3));
            var assessed = new AssessWordsViewModel();
            assessed.Load([new AssessmentWordResult("na", "no-analysis", false, "Search completed", 4, null)]);

            await ShowAssessmentAsync(fake, assessed);

            var kitabu = words.Rows.Single(row => row.Form == "kitabu").Listed;
            Assert.Equal(ParserOutcome.NotParsed, kitabu.Row.Outcome);
            Assert.Equal(Mark.Approved, kitabu.Row.OpinionMark);
            Assert.Equal(["kitabu"], kitabu.Row.FieldWorksMorphemes.Select(morph => morph.Form));
            Assert.Equal("×2", kitabu.Row.PlacesText);
            Assert.False(kitabu.HasCard);
            Assert.Equal("Parse all words to link kitabu to Word Analyses", kitabu.Row.WordAnalysesTip);
            kitabu.Row.TryWordCommand.Execute(null);
            Assert.Equal(["kitabu"], tried);

            var na = words.Rows.Single(row => row.Form == "na").Listed;
            Assert.Equal(assessed.Find("na")!.WordRow.Outcome, na.Row.Outcome);
            Assert.Equal(ProjectStanding.NotPresent, na.Row.Row.Opinion);
            Assert.Equal("×1", na.Row.PlacesText);
            Assert.True(na.HasCard);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void CheckingATextReloadsWordsForTheNewlyChosenTexts()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var context = WorkspaceContextTests.NewContext(fake);
            var page = new TextsPageModel(context);
            var source = SelectionModelFixture.WithOccurrenceTexts(new TextWordsResponse(
                [new TextWord("kitabu", null,
                    [new WordOccurrence(TextId, "Alpha", 1, "kitabu", "approved", Analysis("book", "book"))],
                    [Analysis("book", "book")], [])], [], true));
            await using var fixture = await WorkspaceContextTests.OpenCapturedSelectionAsync(fake, context, source);
            context.Selection.Texts[0].IsChecked = false;
            await context.EvidencePublication;
            Assert.Empty(page.Words.Rows);
            var previous = context.SelectionReads.Reader!;
            var before = fake.OpenSelectionReaderRequests.Count;

            context.Selection.Texts[0].IsChecked = true;
            await context.EvidencePublication;

            Assert.Equal(before + 1, fake.OpenSelectionReaderRequests.Count);
            Assert.True(previous.Diagnostics.IsDisposed);
            Assert.Equal([TextId], context.SelectionReads.Reader!.Context.TextIds);
            Assert.Equal("kitabu", Assert.Single(page.Words.Rows).Form);
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void AWordWithOneApprovedAnalysisAtEveryOccurrenceIsApproved()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, selection, words) = NewViewModel();
            await words.SetProjectAsync(ProjectPath);
            await PublishAsync(fake, new TextWordsResponse(
                [new TextWord("kitabu", null,
                    [new WordOccurrence(TextId, "Alpha", 1, "kitabu.", "approved", Analysis("k1", "book")),
                     new WordOccurrence(TextId, "Alpha", 4, "kitabu tena.", "approved", Analysis("k1", "book"))],
                    [Analysis("k1", "book")], [])],
                [], HasBaseline: true));


            var row = Assert.Single(words.Rows);
            Assert.Equal(WordProjectStatus.Approved, row.Status);
            Assert.Equal("Approved", row.StatusLabel);
            Assert.Equal(2, row.OccurrenceCount);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void AWordWithDifferentChosenAnalysesAcrossOccurrencesHasSeveral_AsHomographsDo()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, selection, words) = NewViewModel();
            await words.SetProjectAsync(ProjectPath);
            await PublishAsync(fake, new TextWordsResponse(
                [new TextWord("anapenda", null,
                    [new WordOccurrence(TextId, "Alpha", 2, "s1", "approved", Analysis("love", "love")),
                     new WordOccurrence(TextId, "Alpha", 9, "s2", "approved", Analysis("like", "like"))],
                    [Analysis("love", "love"), Analysis("like", "like")], [])],
                [], HasBaseline: true));


            var row = Assert.Single(words.Rows);
            Assert.Equal(WordProjectStatus.Approved, row.Status);
            Assert.True(row.HasSeveralAnalyses);
            Assert.Equal("Approved, 2 analyses", row.StatusLabel);
            Assert.Equal(Mark.Approved, row.StatusMark);
            Assert.Equal(1, words.ApprovedFilterCount);
            Assert.Equal(1, words.SeveralFilterCount);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void AWordWithNoAnalysisAnywhereIsNotPresent()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, selection, words) = NewViewModel();
            await words.SetProjectAsync(ProjectPath);
            await PublishAsync(fake, new TextWordsResponse(
                [new TextWord("nitakupa", null, [new WordOccurrence(TextId, "Alpha", 5, "s", "unanalysed", null)], [], [])],
                [], HasBaseline: true));


            var row = Assert.Single(words.Rows);
            Assert.Equal(WordProjectStatus.NotPresent, row.Status);
            Assert.Equal("Not analysed in the project", row.ProjectSummary);
            Assert.Equal("Not present", row.StatusLabel);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void SeveralAnalysisSummaryNamesOnlyTheAnalysesUsedInChosenTexts()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, _, words) = NewViewModel();
            await words.SetProjectAsync(ProjectPath);
            var book = Analysis("book", "book");
            var child = Analysis("child", "child");
            var unused = Analysis("unused", "unused");
            await PublishAsync(fake, new TextWordsResponse(
                [new TextWord("word", Guid.NewGuid().ToString(),
                    [new WordOccurrence(TextId, "Alpha", 1, "word", "approved", book),
                     new WordOccurrence(TextId, "Alpha", 2, "word", "approved", child)],
                    [book, child, unused], [])], [], true));
            var row = Assert.Single(words.Rows);
            Assert.True(row.HasSeveralAnalyses);
            Assert.Contains("book", row.ProjectSummary);
            Assert.Contains("child", row.ProjectSummary);
            Assert.DoesNotContain("unused", row.ProjectSummary);
            Assert.Equal(3, row.ApprovedAnalyses.Count);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void HandOffHelpTextExplainsWhichWordsWillBeSent()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, _, words) = NewViewModel();
            await words.SetProjectAsync(ProjectPath);
            await PublishAsync(fake, new TextWordsResponse(
                [new TextWord("kitabu", null, [new WordOccurrence(TextId, "Alpha", 1, "s", "unanalysed", null)], [], []),
                 new TextWord("nitakupa", null, [new WordOccurrence(TextId, "Alpha", 2, "s", "unanalysed", null)], [], [])],
                [], HasBaseline: true));


            Assert.Equal("Tick words first.", words.HandOffCheckedWordsHelpText);
            words.Rows[0].IsChecked = true;
            Assert.Empty(words.HandOffCheckedWordsHelpText);
            words.Rows[1].IsChecked = true;
            Assert.Empty(words.HandOffCheckedWordsHelpText);
        }, TimeSpan.FromSeconds(180));
    }

    [Theory]
    [InlineData(2, 0, false, WordProjectStatus.Candidate, "Unknown")]
    [InlineData(0, 1, false, WordProjectStatus.Rejected, "Disapproved")]
    [InlineData(1, 1, false, WordProjectStatus.Candidate, "Unknown")]
    [InlineData(1, 0, true, WordProjectStatus.IncorrectSpelling, "Incorrect spelling")]
    public void AWordWithoutAnApprovedAnalysisTakesTheBestStandingItHas(
        int candidates, int rejected, bool incorrectSpelling, WordProjectStatus expected, string label)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, selection, words) = NewViewModel();
            await words.SetProjectAsync(ProjectPath);
            await PublishAsync(fake, new TextWordsResponse(
                [new TextWord("kitanda", null, [new WordOccurrence(TextId, "Alpha", 4, "s", "unanalysed", null)], [],
                    Enumerable.Repeat(Analysis("k9", "bed"), rejected).ToArray(), candidates, incorrectSpelling)],
                [], HasBaseline: true));


            var row = Assert.Single(words.Rows);
            Assert.Equal(expected, row.Status);
            Assert.Equal(label, row.StatusLabel);
            Assert.Equal(WordProjectStatuses.MarkOf(expected), row.StatusMark);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void WhatTheProjectHoldsWearsItsOpinionMarkAndAnIncorrectSpellingNone()
    {
        Assert.Equal(Mark.Approved, WordProjectStatuses.MarkOf(WordProjectStatus.Approved));
        Assert.Equal(Mark.Unknown, WordProjectStatuses.MarkOf(WordProjectStatus.Candidate));
        Assert.Equal(Mark.NotInFieldWorks, WordProjectStatuses.MarkOf(WordProjectStatus.NotPresent));
        Assert.Equal(Mark.Disapproved, WordProjectStatuses.MarkOf(WordProjectStatus.Rejected));
        Assert.Null(WordProjectStatuses.MarkOf(WordProjectStatus.IncorrectSpelling));
    }

    [Fact]
    public void AnOccurrenceWithAnUnapprovedAnalysisMakesTheWordACandidate()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, selection, words) = NewViewModel();
            await words.SetProjectAsync(ProjectPath);
            await PublishAsync(fake, new TextWordsResponse(
                [new TextWord("chakula", null, [new WordOccurrence(TextId, "Alpha", 6, "s", "unapproved", Analysis("c1", "food"))], [], [])],
                [], HasBaseline: true));


            Assert.Equal(WordProjectStatus.Candidate, Assert.Single(words.Rows).Status);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void TheSeveralAnalysesChipReplacesTheStatusFilterAndAllClearsBoth()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, selection, words) = NewViewModel();
            await words.SetProjectAsync(ProjectPath);
            await PublishAsync(fake, new TextWordsResponse(
                [
                    new TextWord("anapenda", null,
                        [new WordOccurrence(TextId, "Alpha", 1, "s", "approved", Analysis("love", "love")),
                         new WordOccurrence(TextId, "Alpha", 2, "s", "approved", Analysis("like", "like"))],
                        [Analysis("love", "love"), Analysis("like", "like")], []),
                    new TextWord("nitakupa", null, [new WordOccurrence(TextId, "Alpha", 3, "s", "unanalysed", null)], [], []),
                ],
                [], HasBaseline: true, OccurrenceCount: 3));
            words.SetStatusFilterCommand.Execute(WordProjectStatus.NotPresent);

            words.ShowSeveralCommand.Execute(null);

            Assert.Null(words.StatusFilter);
            Assert.False(words.IsAllFilter);
            Assert.Equal("anapenda", Assert.Single(words.Rows).Form);

            words.SetStatusFilterCommand.Execute(null);

            Assert.False(words.SeveralOnly);
            Assert.True(words.IsAllFilter);
            Assert.Equal(2, words.Rows.Count);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void TheSummaryCountsWordsAndPlaces()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, selection, words) = NewViewModel();
            await words.SetProjectAsync(ProjectPath);
            await PublishAsync(fake, new TextWordsResponse(
                [
                    new TextWord("kitabu", null,
                        [new WordOccurrence(TextId, "Alpha", 1, "s", "approved", Analysis("k1", "book")),
                         new WordOccurrence(TextId, "Alpha", 2, "s", "approved", Analysis("k1", "book"))],
                        [Analysis("k1", "book")], []),
                    new TextWord("nitakupa", null, [new WordOccurrence(TextId, "Alpha", 3, "s", "unanalysed", null)], [], []),
                ],
                [], HasBaseline: true, OccurrenceCount: 3));


            Assert.Equal("2 words · 3 places", words.SummaryText);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void TheSummaryUsesThePhysicalOccurrenceTotalFromTheReader()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, _, words) = NewViewModel();
            await words.SetProjectAsync(ProjectPath);
            await PublishAsync(fake, new TextWordsResponse(
                [new TextWord("kitabu", null,
                    Enumerable.Range(1, 17).Select(number => new WordOccurrence(TextId, "Alpha", number, "s", "approved", Analysis("k1", "book"))).ToArray(),
                    [Analysis("k1", "book")], [])],
                [], HasBaseline: true, OccurrenceCount: 17));


            Assert.Equal("1 word · 17 places", words.SummaryText);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void SelectingTextsShowsItsWordCountAfterTheReadAndCountsSpellingsOnce()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, selection, words) = NewViewModel();
            await PublishAsync(fake, new TextWordsResponse([], [], HasBaseline: true));
            await words.SetProjectAsync(ProjectPath);
            fake.ListTextsCompletesWith(new TextInventoryResponse(
                [new TextChoiceSummary(TextId, "Alpha")], HasBaseline: true));
            await selection.SetProjectAsync(ProjectPath);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = new TaskCompletionSource<SIL.Motif.Contract.Commands.CommandOutcome<SIL.Motif.Commands.SelectionReading.SelectionReader>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            fake.SelectionReaderHandler = (_, _) => { entered.SetResult(); return pending.Task; };
            var load = fake.ReaderOwner.ReloadAsync(ProjectPath, [TextId], []);
            await entered.Task;
            Assert.Equal("Reading words in selected texts…", words.SummaryText);
            var firstId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011");
            var secondId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000099");
            var sameForm = new TextWord("kitabu", firstId.ToString("D"),
                [new WordOccurrence(TextId, "Alpha", 1, "kitabu", "unanalysed", null)], [], []);
            var otherIdentity = sameForm with { WordformGuid = secondId.ToString("D"),
                Occurrences = [new WordOccurrence(TextId, "Alpha", 2, "kitabu", "unanalysed", null)] };
            using var store = SIL.Motif.Tests.TestFixtures.StoredSelectionFixture.FromDisplayRecords(
                SelectionModelFixture.WithOccurrenceTexts(new TextWordsResponse([sameForm, otherIdentity], [], true)));
            pending.SetResult(await store.OpenAsync(new(store.ProjectPath, [TextId], [])));
            await load;

            Assert.Equal(2, words.Rows.Count);
            Assert.Equal(1, words.WordCount);
            Assert.Equal("1 word · 2 places", words.SummaryText);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void TheSummaryUsesThousandsSeparatorsForWordsAndPlaces()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, _, words) = NewViewModel();
            await words.SetProjectAsync(ProjectPath);
            var rows = Enumerable.Range(0, 1000).Select(index => new TextWord($"word{index}", null,
                Enumerable.Range(0, index < 200 ? 2 : 1).Select(extra =>
                    new WordOccurrence(TextId, "Alpha", index + 1 + extra * 1000, $"word{index}", "unanalysed", null)).ToArray(), [], [])).ToArray();
            await PublishAsync(fake, new TextWordsResponse(rows, [], HasBaseline: true, OccurrenceCount: 1200));


            Assert.Equal($"{1000.ToString("N0", CultureInfo.CurrentCulture)} words · " +
                $"{1200.ToString("N0", CultureInfo.CurrentCulture)} places", words.SummaryText);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void TheStatusFilterShowsOnlyMatchingRows()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, selection, words) = NewViewModel();
            await words.SetProjectAsync(ProjectPath);
            await PublishAsync(fake, new TextWordsResponse(
                [
                    new TextWord("kitabu", null,
                        [new WordOccurrence(TextId, "Alpha", 1, "s", "approved", Analysis("k1", "book"))],
                        [Analysis("k1", "book")], []),
                    new TextWord("nitakupa", null, [new WordOccurrence(TextId, "Alpha", 2, "s", "unanalysed", null)], [], []),
                ],
                [], HasBaseline: true));

            words.SetStatusFilterCommand.Execute(WordProjectStatus.NotPresent);

            Assert.Single(words.Rows);
            Assert.Equal("nitakupa", words.Rows[0].Form);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void ReloadingFeedsPerTextCountsBackOntoTheSelectionsTextChoices()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, selection, words) = NewViewModel();
            fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], HasBaseline: true));
            await selection.SetProjectAsync(ProjectPath);
            await words.SetProjectAsync(ProjectPath);
            selection.Texts[0].IsChecked = true;
            await PublishAsync(fake, new TextWordsResponse(
                [new TextWord("kitabu", null,
                    [new WordOccurrence(TextId, "Alpha", 1, "s", "approved", Analysis("k1", "book")),
                     new WordOccurrence(TextId, "Alpha", 2, "s", "approved", Analysis("k1", "book"))],
                    [Analysis("k1", "book")], [])],
                [], HasBaseline: true));


            Assert.Equal("2 words · 1 distinct", selection.Texts[0].CountsText);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void MultilingualTextCountsKeepFormMembershipsSeparateFromDisplayedSpellings()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, selection, words) = NewViewModel();
            fake.ListTextsCompletesWith(new TextInventoryResponse(
                [new TextChoiceSummary(TextId, "Alpha", WordCount: 2)], HasBaseline: true));
            await selection.SetProjectAsync(ProjectPath);
            await words.SetProjectAsync(ProjectPath);
            selection.Texts[0].IsChecked = true;
            var wordformId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011");
            var token = new TextWordsProjectedToken("same",
                [new WritingSystemText("same", "qaa"), new WritingSystemText("same", "qaa-x-second")],
                wordformId, "unanalysed", null, null, null, null, 0, null);
            using var store = new StoredSelectionFixture(new TextWordsProjection(
                [new TextWordsProjectedText(TextId, "Alpha",
                    [new TextWordsProjectedLine(1, "same", [token], Guid.NewGuid(), Guid.NewGuid(), true)], [])],
                [new TextWordsProjectedWordform(wordformId, [], [], 0, false, [])]));
            fake.SelectionReaderHandler = store.OpenAsync;
            await fake.ReaderOwner.ReloadAsync(store.ProjectPath, [TextId], []);
            Assert.Null(fake.ReaderOwner.Refusal);

            Assert.Equal(1, words.WordCount);
            Assert.Equal(1, words.OccurrenceCount);
            Assert.Equal("1 word · 1 place", words.SummaryText);
            Assert.Equal(2, words.Rows.Count);
            Assert.Equal(new string?[] { "qaa", "qaa-x-second" },
                words.Rows.Select(row => row.FormWritingSystem).Order());
            Assert.Equal("1 words · 1 distinct", selection.Texts[0].CountsText);
            await fake.ReaderOwner.StopAsync();
        }, TimeSpan.FromSeconds(180));
    }
}
