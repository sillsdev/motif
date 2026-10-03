using System.Linq;
using System.Globalization;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins <see cref="TextWordsViewModel"/>: it reloads whenever the checked Texts change, computes each
/// word's project status (None, Approved, Several analyses), the toolbar's summary counts, and feeds
/// <see cref="TextChoiceViewModel"/>'s own counts.
/// </summary>
public sealed class TextWordsViewModelTests
{
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

    private static (FakeCommandClient Fake, SelectionViewModel Selection, TextWordsViewModel Words) NewViewModel()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var words = new TextWordsViewModel(fake, selection);
        return (fake, selection, words);
    }

    [Fact]
    public async Task ReloadingCountsLargeTextsWithoutMaterializingEveryOccurrenceRow()
    {
        var (fake, selection, words) = NewViewModel();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Story")], HasBaseline: true));
        await selection.SetProjectAsync(ProjectPath);
        await words.SetProjectAsync(ProjectPath);
        selection.Texts[0].IsChecked = true;
        var analysis = Analysis("book", "book");
        var occurrences = Enumerable.Range(0, 500).Select(index =>
            new WordOccurrence(TextId, "Story", index + 1, "kitabu", "approved", analysis)).ToArray();
        var source = Enumerable.Range(0, 2000).Select(index => new TextWord($"word-{index}", null,
            occurrences, [analysis], []) { Analyses = [analysis] }).ToArray();
        fake.ListTextWordsCompletesWith(new TextWordsResponse(source, [], true, OccurrenceCount: 1_000_000));
        var before = GC.GetAllocatedBytesForCurrentThread();
        await words.ReloadAsync();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 32 * 1024 * 1024, $"Reloading allocated {allocated:N0} bytes before a row was read.");
        Assert.Equal(2000, words.Rows.Count);
        Assert.Equal($"{1_000_000:N0} words · {2_000:N0} distinct", selection.Texts[0].CountsText);
        Assert.Equal(500, words.Rows[0].Occurrences.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoredAssessmentKeepsEachVisibleHomographsOwnProjectFacts(bool reverse)
    {
        var firstId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011");
        var otherId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000099");
        var approved = Analysis("book", "book") with
            { StoredAnalysisId = "book-id", StoredAnalysisOpinion = ReadingGrade.Approved };
        var rejected = Analysis("love", "love") with
            { StoredAnalysisId = "love-id", StoredAnalysisOpinion = ReadingGrade.Disapproved };
        var first = new TextWord("kitabu", firstId.ToString(),
            [new WordOccurrence(TextId, "Alpha", 1, "kitabu", "approved", approved)], [approved], [])
            { Analyses = [approved] };
        var other = new TextWord("kitabu", otherId.ToString(),
            [new WordOccurrence(TextId, "Alpha", 2, "kitabu", "disapproved", rejected)], [], [rejected])
            { Analyses = [rejected] };
        var (fake, _, words) = NewViewModel();
        fake.ListTextWordsCompletesWith(new TextWordsResponse(reverse ? [other, first] : [first, other], [], true));
        await words.SetProjectAsync(ProjectPath);
        await words.ReloadAsync();
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

        words.ShowAssessment(assessed.Find);

        Assert.Equal(Mark.Approved, own.Listed.Row.OpinionMark);
        Assert.Equal(Mark.Disapproved, homograph.Listed.Row.OpinionMark);
        Assert.Equal("book", own.Listed.Row.Gloss);
        Assert.Equal("love", homograph.Listed.Row.Gloss);
        Assert.Equal("love-id", Assert.Single(homograph.Listed.Card!.Marking.FieldWorksAnalyses).StoredAnalysisId);
        Assert.Equal(ReadingGrade.Disapproved,
            Assert.Single(homograph.Listed.Card.Marking.FieldWorksAnalyses).Opinion);
        Assert.NotEqual(ParserOutcome.Same, homograph.Listed.Row.Outcome);
        Assert.Equal("×1", homograph.Listed.Row.PlacesText);
        Assert.Equal(Mark.Disapproved, homograph.Listed.Card!.WordRow.OpinionMark);
        Assert.Equal("love", homograph.Listed.Card.WordRow.Gloss);
        Assert.NotEqual(assessed.Find("kitabu")!.Comparison, homograph.Listed.Card.Source.Comparison);
        words.ShowAssessment(null);
        Assert.Equal("love", homograph.Listed.Row.Gloss);
    }

    [Fact]
    public async Task WordsAreListedMostFrequentFirstWithTheLatestAssessmentsResult()
    {
        var (fake, _, words) = NewViewModel();
        await words.SetProjectAsync(ProjectPath);
        fake.ListTextWordsCompletesWith(new TextWordsResponse(
            [new TextWord("mara", null, [new WordOccurrence(TextId, "Alpha", 1, "s", "unanalysed", null)], [], []),
             new TextWord("na", null,
                [new WordOccurrence(TextId, "Alpha", 2, "s", "unanalysed", null),
                 new WordOccurrence(TextId, "Alpha", 3, "s", "unanalysed", null)], [], [])],
            [], HasBaseline: true, OccurrenceCount: 3));
        var assessed = new AssessWordsViewModel();
        assessed.Load([new AssessmentWordResult("na", "no-analysis", false, "Search completed", 4, null)]);

        await words.ReloadAsync();
        words.ShowAssessment(assessed.Find);

        Assert.Equal(["na", "mara"], words.Rows.Select(row => row.Form));
        Assert.Equal("No parse", words.Rows[0].LastResultLabel);
        Assert.False(words.Rows[1].HasLastResult);
        Assert.Equal("Not present", words.Rows[1].StatusLabel);
    }

    [Fact]
    public async Task EachWordsRowIsTheParsedRowOnceAParseReachesIt_AndWhatFieldWorksHoldsUntilThen()
    {
        var (fake, _, words) = NewViewModel();
        var tried = new List<string>();
        words.WordRowRoutes = new WordRowRoutes { TryWord = tried.Add };
        await words.SetProjectAsync(ProjectPath);
        fake.ListTextWordsCompletesWith(new TextWordsResponse(
            [new TextWord("kitabu", null,
                [new WordOccurrence(TextId, "Alpha", 1, "kitabu.", "approved", Analysis("k1", "book")),
                 new WordOccurrence(TextId, "Alpha", 2, "kitabu.", "approved", Analysis("k1", "book"))],
                [Analysis("k1", "book")], []),
             new TextWord("na", null, [new WordOccurrence(TextId, "Alpha", 3, "s", "unanalysed", null)], [], [])],
            [], HasBaseline: true, OccurrenceCount: 3));
        var assessed = new AssessWordsViewModel();
        assessed.Load([new AssessmentWordResult("na", "no-analysis", false, "Search completed", 4, null)]);

        await words.ReloadAsync();
        words.ShowAssessment(assessed.Find);

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
    }

    [Fact]
    public async Task CheckingATextReloadsWordsForTheNewlyChosenTexts()
    {
        var (fake, selection, words) = NewViewModel();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], HasBaseline: true));
        await selection.SetProjectAsync(ProjectPath);
        await words.SetProjectAsync(ProjectPath);
        fake.ListTextWordsCompletesWith(new TextWordsResponse(
            [new TextWord("kitabu", null, [new WordOccurrence(TextId, "Alpha", 1, "kitabu.", "approved", Analysis("k1", "book"))],
                [Analysis("k1", "book")], [])],
            [], HasBaseline: true));

        selection.Texts[0].IsChecked = true;
        await Task.Yield();

        Assert.Single(fake.ListTextWordsRequests, request => request.TextIds.SequenceEqual([TextId]));
        Assert.Equal(1, words.WordCount);
    }

    [Fact]
    public async Task AWordWithOneApprovedAnalysisAtEveryOccurrenceIsApproved()
    {
        var (fake, selection, words) = NewViewModel();
        await words.SetProjectAsync(ProjectPath);
        fake.ListTextWordsCompletesWith(new TextWordsResponse(
            [new TextWord("kitabu", null,
                [new WordOccurrence(TextId, "Alpha", 1, "kitabu.", "approved", Analysis("k1", "book")),
                 new WordOccurrence(TextId, "Alpha", 4, "kitabu tena.", "approved", Analysis("k1", "book"))],
                [Analysis("k1", "book")], [])],
            [], HasBaseline: true));

        await words.ReloadAsync();

        var row = Assert.Single(words.Rows);
        Assert.Equal(WordProjectStatus.Approved, row.Status);
        Assert.Equal("Approved", row.StatusLabel);
        Assert.Equal(2, row.OccurrenceCount);
    }

    [Fact]
    public async Task AWordWithDifferentChosenAnalysesAcrossOccurrencesHasSeveral_AsHomographsDo()
    {
        var (fake, selection, words) = NewViewModel();
        await words.SetProjectAsync(ProjectPath);
        fake.ListTextWordsCompletesWith(new TextWordsResponse(
            [new TextWord("anapenda", null,
                [new WordOccurrence(TextId, "Alpha", 2, "s1", "approved", Analysis("love", "love")),
                 new WordOccurrence(TextId, "Alpha", 9, "s2", "approved", Analysis("like", "like"))],
                [Analysis("love", "love"), Analysis("like", "like")], [])],
            [], HasBaseline: true));

        await words.ReloadAsync();

        var row = Assert.Single(words.Rows);
        Assert.Equal(WordProjectStatus.Approved, row.Status);
        Assert.True(row.HasSeveralAnalyses);
        Assert.Equal("Approved, 2 analyses", row.StatusLabel);
        Assert.Equal(Mark.Approved, row.StatusMark);
        Assert.Equal(1, words.ApprovedFilterCount);
        Assert.Equal(1, words.SeveralFilterCount);
    }

    [Fact]
    public async Task AWordWithNoAnalysisAnywhereIsNotPresent()
    {
        var (fake, selection, words) = NewViewModel();
        await words.SetProjectAsync(ProjectPath);
        fake.ListTextWordsCompletesWith(new TextWordsResponse(
            [new TextWord("nitakupa", null, [new WordOccurrence(TextId, "Alpha", 5, "s", "unanalysed", null)], [], [])],
            [], HasBaseline: true));

        await words.ReloadAsync();

        var row = Assert.Single(words.Rows);
        Assert.Equal(WordProjectStatus.NotPresent, row.Status);
        Assert.Equal("Not analysed in the project", row.ProjectSummary);
        Assert.Equal("Not present", row.StatusLabel);
    }

    [Fact]
    public async Task HandOffHelpTextExplainsWhichWordsWillBeSent()
    {
        var (fake, _, words) = NewViewModel();
        await words.SetProjectAsync(ProjectPath);
        fake.ListTextWordsCompletesWith(new TextWordsResponse(
            [new TextWord("kitabu", null, [new WordOccurrence(TextId, "Alpha", 1, "s", "unanalysed", null)], [], []),
             new TextWord("nitakupa", null, [new WordOccurrence(TextId, "Alpha", 2, "s", "unanalysed", null)], [], [])],
            [], HasBaseline: true));

        await words.ReloadAsync();

        Assert.Equal("Tick words first.", words.HandOffCheckedWordsHelpText);
        words.Rows[0].IsChecked = true;
        Assert.Empty(words.HandOffCheckedWordsHelpText);
        words.Rows[1].IsChecked = true;
        Assert.Empty(words.HandOffCheckedWordsHelpText);
    }

    [Theory]
    [InlineData(2, 0, false, WordProjectStatus.Candidate, "Unknown")]
    [InlineData(0, 1, false, WordProjectStatus.Rejected, "Disapproved")]
    [InlineData(1, 1, false, WordProjectStatus.Candidate, "Unknown")]
    [InlineData(1, 0, true, WordProjectStatus.IncorrectSpelling, "Incorrect spelling")]
    public async Task AWordWithoutAnApprovedAnalysisTakesTheBestStandingItHas(
        int candidates, int rejected, bool incorrectSpelling, WordProjectStatus expected, string label)
    {
        var (fake, selection, words) = NewViewModel();
        await words.SetProjectAsync(ProjectPath);
        fake.ListTextWordsCompletesWith(new TextWordsResponse(
            [new TextWord("kitanda", null, [new WordOccurrence(TextId, "Alpha", 4, "s", "unanalysed", null)], [],
                Enumerable.Repeat(Analysis("k9", "bed"), rejected).ToArray(), candidates, incorrectSpelling)],
            [], HasBaseline: true));

        await words.ReloadAsync();

        var row = Assert.Single(words.Rows);
        Assert.Equal(expected, row.Status);
        Assert.Equal(label, row.StatusLabel);
        Assert.Equal(WordProjectStatuses.MarkOf(expected), row.StatusMark);
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
    public async Task AnOccurrenceWithAnUnapprovedAnalysisMakesTheWordACandidate()
    {
        var (fake, selection, words) = NewViewModel();
        await words.SetProjectAsync(ProjectPath);
        fake.ListTextWordsCompletesWith(new TextWordsResponse(
            [new TextWord("chakula", null, [new WordOccurrence(TextId, "Alpha", 6, "s", "unapproved", Analysis("c1", "food"))], [], [])],
            [], HasBaseline: true));

        await words.ReloadAsync();

        Assert.Equal(WordProjectStatus.Candidate, Assert.Single(words.Rows).Status);
    }

    [Fact]
    public async Task TheSeveralAnalysesChipReplacesTheStatusFilterAndAllClearsBoth()
    {
        var (fake, selection, words) = NewViewModel();
        await words.SetProjectAsync(ProjectPath);
        fake.ListTextWordsCompletesWith(new TextWordsResponse(
            [
                new TextWord("anapenda", null,
                    [new WordOccurrence(TextId, "Alpha", 1, "s", "approved", Analysis("love", "love")),
                     new WordOccurrence(TextId, "Alpha", 2, "s", "approved", Analysis("like", "like"))],
                    [Analysis("love", "love"), Analysis("like", "like")], []),
                new TextWord("nitakupa", null, [new WordOccurrence(TextId, "Alpha", 3, "s", "unanalysed", null)], [], []),
            ],
            [], HasBaseline: true, OccurrenceCount: 3));
        await words.ReloadAsync();
        words.SetStatusFilterCommand.Execute(WordProjectStatus.NotPresent);

        words.ShowSeveralCommand.Execute(null);

        Assert.Null(words.StatusFilter);
        Assert.False(words.IsAllFilter);
        Assert.Equal("anapenda", Assert.Single(words.Rows).Form);

        words.SetStatusFilterCommand.Execute(null);

        Assert.False(words.SeveralOnly);
        Assert.True(words.IsAllFilter);
        Assert.Equal(2, words.Rows.Count);
    }

    [Fact]
    public async Task TheSummaryCountsWordsAndPlaces()
    {
        var (fake, selection, words) = NewViewModel();
        await words.SetProjectAsync(ProjectPath);
        fake.ListTextWordsCompletesWith(new TextWordsResponse(
            [
                new TextWord("kitabu", null,
                    [new WordOccurrence(TextId, "Alpha", 1, "s", "approved", Analysis("k1", "book")),
                     new WordOccurrence(TextId, "Alpha", 2, "s", "approved", Analysis("k1", "book"))],
                    [Analysis("k1", "book")], []),
                new TextWord("nitakupa", null, [new WordOccurrence(TextId, "Alpha", 3, "s", "unanalysed", null)], [], []),
            ],
            [], HasBaseline: true, OccurrenceCount: 3));

        await words.ReloadAsync();

        Assert.Equal("2 words · 3 places", words.SummaryText);
    }

    [Fact]
    public async Task TheSummaryUsesTheOccurrenceTotalReturnedByTheCommand()
    {
        var (fake, _, words) = NewViewModel();
        await words.SetProjectAsync(ProjectPath);
        fake.ListTextWordsCompletesWith(new TextWordsResponse(
            [new TextWord("kitabu", null,
                [new WordOccurrence(TextId, "Alpha", 1, "s", "approved", Analysis("k1", "book"))],
                [Analysis("k1", "book")], [])],
            [], HasBaseline: true, OccurrenceCount: 17));

        await words.ReloadAsync();

        Assert.Equal("1 word · 17 places", words.SummaryText);
    }

    [Fact]
    public async Task SelectingTextsShowsItsWordCountAfterTheReadAndCountsSpellingsOnce()
    {
        var (fake, selection, words) = NewViewModel();
        fake.ListTextWordsCompletesWith(new TextWordsResponse([], [], HasBaseline: true));
        await words.SetProjectAsync(ProjectPath);
        fake.ListTextsCompletesWith(new TextInventoryResponse(
            [new TextChoiceSummary(TextId, "Alpha")], HasBaseline: true));
        await selection.SetProjectAsync(ProjectPath);
        var pending = new TaskCompletionSource<CommandOutcome<TextWordsResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.OnListTextWords((_, _) => pending.Task);

        selection.Texts[0].IsChecked = true;

        Assert.Equal("Reading words in selected texts…", words.SummaryText);
        var firstId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011");
        var secondId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000099");
        var sameForm = new TextWord("kitabu", firstId.ToString("D"),
            [new WordOccurrence(TextId, "Alpha", 1, "kitabu", "unanalysed", null)], [], []);
        var otherIdentity = sameForm with
        {
            WordformGuid = secondId.ToString("D"),
            Occurrences = [new WordOccurrence(TextId, "Alpha", 2, "kitabu", "unanalysed", null)],
        };
        pending.SetResult(CommandOutcome<TextWordsResponse>.Success(new TextWordsResponse(
            [sameForm, otherIdentity], [], HasBaseline: true, OccurrenceCount: 2)));
        await words.ReloadCommand.ExecutionTask!;

        Assert.Equal(2, words.Rows.Count);
        Assert.Equal(1, words.WordCount);
        Assert.Equal("1 word · 2 places", words.SummaryText);
    }

    [Fact]
    public async Task TheSummaryUsesThousandsSeparatorsForWordsAndPlaces()
    {
        var (fake, _, words) = NewViewModel();
        await words.SetProjectAsync(ProjectPath);
        var rows = Enumerable.Range(0, 1000).Select(index => new TextWord($"word{index}", null,
            [new WordOccurrence(TextId, "Alpha", index + 1, $"word{index}", "unanalysed", null)], [], [])).ToArray();
        fake.ListTextWordsCompletesWith(new TextWordsResponse(rows, [], HasBaseline: true, OccurrenceCount: 1200));

        await words.ReloadAsync();

        Assert.Equal($"{1000.ToString("N0", CultureInfo.CurrentCulture)} words · " +
            $"{1200.ToString("N0", CultureInfo.CurrentCulture)} places", words.SummaryText);
    }

    [Fact]
    public async Task TheStatusFilterShowsOnlyMatchingRows()
    {
        var (fake, selection, words) = NewViewModel();
        await words.SetProjectAsync(ProjectPath);
        fake.ListTextWordsCompletesWith(new TextWordsResponse(
            [
                new TextWord("kitabu", null,
                    [new WordOccurrence(TextId, "Alpha", 1, "s", "approved", Analysis("k1", "book"))],
                    [Analysis("k1", "book")], []),
                new TextWord("nitakupa", null, [new WordOccurrence(TextId, "Alpha", 2, "s", "unanalysed", null)], [], []),
            ],
            [], HasBaseline: true));
        await words.ReloadAsync();

        words.SetStatusFilterCommand.Execute(WordProjectStatus.NotPresent);

        Assert.Single(words.Rows);
        Assert.Equal("nitakupa", words.Rows[0].Form);
    }

    [Fact]
    public async Task ReloadingFeedsPerTextCountsBackOntoTheSelectionsTextChoices()
    {
        var (fake, selection, words) = NewViewModel();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], HasBaseline: true));
        await selection.SetProjectAsync(ProjectPath);
        await words.SetProjectAsync(ProjectPath);
        selection.Texts[0].IsChecked = true;
        fake.ListTextWordsCompletesWith(new TextWordsResponse(
            [new TextWord("kitabu", null,
                [new WordOccurrence(TextId, "Alpha", 1, "s", "approved", Analysis("k1", "book")),
                 new WordOccurrence(TextId, "Alpha", 2, "s", "approved", Analysis("k1", "book"))],
                [Analysis("k1", "book")], [])],
            [], HasBaseline: true));

        await words.ReloadAsync();

        Assert.Equal("2 words · 1 distinct", selection.Texts[0].CountsText);
    }
}
