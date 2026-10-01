using System.Linq;
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

    private static (FakeCommandClient Fake, SelectionViewModel Selection, TextWordsViewModel Words) NewViewModel()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var words = new TextWordsViewModel(fake, selection);
        return (fake, selection, words);
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
    public async Task TheSummaryCountsWordsOccurrencesAndApprovedWords()
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

        Assert.Equal("2 words to test · 3 occurrences · 1 with an approved analysis", words.SummaryText);
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

        Assert.Equal("1 word to test · 17 occurrences · 1 with an approved analysis", words.SummaryText);
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
