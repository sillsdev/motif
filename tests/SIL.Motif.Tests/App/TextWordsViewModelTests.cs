using System.Linq;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins <see cref="TextWordsViewModel"/>: it reloads whenever the checked Texts change, computes each
/// word's project status (None, Approved, Several analyses), the toolbar's summary counts, the reader's
/// per-Text lines with each token's status, and feeds <see cref="TextChoiceViewModel"/>'s own counts.
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
            [], HasBaseline: true));
        var assessed = new AssessWordsViewModel();
        assessed.Load([new AssessmentWordResult("na", "no-analysis", false, "Search completed", 4, null)]);

        await words.ReloadAsync();
        words.ShowAssessment(assessed.Find);

        Assert.Equal(["na", "mara"], words.Rows.Select(row => row.Form));
        Assert.Equal("No parse", words.Rows[0].LastResultLabel);
        Assert.False(words.Rows[1].HasLastResult);
        Assert.Equal("Not stored yet", words.Rows[1].StatusLabel);
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
        Assert.Equal(WordProjectStatus.SeveralAnalyses, row.Status);
        Assert.Equal("Several analyses", row.StatusLabel);
    }

    [Fact]
    public async Task AWordWithNoAnalysisAnywhereIsNone()
    {
        var (fake, selection, words) = NewViewModel();
        await words.SetProjectAsync(ProjectPath);
        fake.ListTextWordsCompletesWith(new TextWordsResponse(
            [new TextWord("nitakupa", null, [new WordOccurrence(TextId, "Alpha", 5, "s", "unanalysed", null)], [], [])],
            [], HasBaseline: true));

        await words.ReloadAsync();

        var row = Assert.Single(words.Rows);
        Assert.Equal(WordProjectStatus.None, row.Status);
        Assert.Equal("Not analysed in the project", row.ProjectSummary);
        Assert.Equal("Not stored yet", row.StatusLabel);
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
            [], HasBaseline: true));

        await words.ReloadAsync();

        Assert.Equal("2 words to test · 3 occurrences · 1 with an approved analysis", words.SummaryText);
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

        words.SetStatusFilterCommand.Execute(WordProjectStatus.None);

        Assert.Single(words.Rows);
        Assert.Equal("nitakupa", words.Rows[0].Form);
    }

    [Fact]
    public async Task AReaderWordCarriesItsMorphsGlossesCategoryAndFieldWorksLinks()
    {
        var (fake, _, words) = NewViewModel();
        await words.SetProjectAsync(ProjectPath);
        var analysis = new ProjectAnalysis("k1",
        [
            new ParserReadingMorph("ki-", "cl7", "n", null, false, "silfw://localhost/link?tool=lexiconEdit&guid=a"),
            new ParserReadingMorph("tabu", "", "n", null, false, null),
        ]);
        fake.ListTextWordsCompletesWith(new TextWordsResponse(
            [new TextWord("kitabu", null, [new WordOccurrence(TextId, "Alpha", 1, "kitabu", "approved", analysis)], [analysis], [])],
            [new TextLines(TextId, "Alpha", [new TextLine(1,
            [
                new TextToken("kitabu", "kitabu", "cl7 ?", "approved")
                {
                    Analysis = analysis, WordGloss = "book", Category = "n",
                    WordLink = "silfw://localhost/link?tool=Analyses&guid=w",
                },
                new TextToken("nitakupa", "nitakupa", null, "unanalysed"),
            ])])],
            HasBaseline: true));

        await words.ReloadAsync();

        var tokens = Assert.Single(words.ReaderTexts).Lines[0].Tokens;
        var analysed = tokens[0];
        Assert.True(analysed.HasAnalysis);
        Assert.Equal(["ki-", "tabu"], analysed.Morphs.Select(morph => morph.Form));
        Assert.Equal(["cl7", "?"], analysed.Morphs.Select(morph => morph.GlossOrPlaceholder));
        Assert.True(analysed.Morphs[0].HasLink);
        Assert.True(analysed.Morphs[1].HasNoLink);
        Assert.Equal("book  ·  n", analysed.WordLine);
        Assert.Equal(new Uri("silfw://localhost/link?tool=Analyses&guid=w"), analysed.WordLink);
        Assert.Equal("Open kitabu in FieldWorks", analysed.WordLinkName);

        Assert.True(tokens[1].IsUnanalysed);
        Assert.True(tokens[1].HasNoWordLink);
    }

    [Fact]
    public async Task TheReaderExplainsAnEmptyViewInsteadOfShowingNothing()
    {
        var (fake, _, words) = NewViewModel();
        await words.SetProjectAsync(ProjectPath);
        Assert.Equal("Check a text on the left to read it here.", words.ReaderMessage);

        fake.ListTextWordsCompletesWith(new TextWordsResponse([], [new TextLines(TextId, "Alpha", [])], HasBaseline: true));
        await words.ReloadAsync();

        Assert.StartsWith("This text has no lines split into words yet.", words.ReaderMessage, StringComparison.Ordinal);
        Assert.True(words.HasReaderMessage);
    }

    [Fact]
    public async Task ReaderTextsCarryEachTokensStatusFromTheSameWordData()
    {
        var (fake, selection, words) = NewViewModel();
        await words.SetProjectAsync(ProjectPath);
        fake.ListTextWordsCompletesWith(new TextWordsResponse(
            [new TextWord("kitabu", null,
                [new WordOccurrence(TextId, "Alpha", 1, "kitabu.", "approved", Analysis("k1", "book"))],
                [Analysis("k1", "book")], [])],
            [new TextLines(TextId, "Alpha",
                [new TextLine(1, [new TextToken("kitabu", "kitabu", "book", "approved"), new TextToken(".", null, null, null)])])],
            HasBaseline: true));

        await words.ReloadAsync();

        var readerText = Assert.Single(words.ReaderTexts);
        var line = Assert.Single(readerText.Lines);
        Assert.Equal(2, line.Tokens.Count);
        Assert.True(line.Tokens[0].IsWord);
        Assert.Equal(WordProjectStatus.Approved, line.Tokens[0].Status);
        Assert.False(line.Tokens[1].IsWord);
        Assert.Null(line.Tokens[1].Status);
    }

    [Fact]
    public async Task SelectingAReaderTokenShowsItsOccurrenceInTheSidePanel()
    {
        var (fake, selection, words) = NewViewModel();
        await words.SetProjectAsync(ProjectPath);
        fake.ListTextWordsCompletesWith(new TextWordsResponse(
            [new TextWord("kitabu", null,
                [new WordOccurrence(TextId, "Alpha", 1, "kitabu.", "approved", Analysis("k1", "book"))],
                [Analysis("k1", "book")], [])],
            [new TextLines(TextId, "Alpha",
                [new TextLine(1, [new TextToken("kitabu", "kitabu", "book", "approved")])])],
            HasBaseline: true));
        await words.ReloadAsync();
        var token = words.ReaderTexts[0].Lines[0].Tokens[0];

        words.SelectToken(token);

        Assert.NotNull(words.SelectedOccurrence);
        Assert.Equal("kitabu", words.SelectedOccurrence!.Form);
        Assert.Equal("Occurrence 1 of 1 · Alpha, line 1", words.SelectedOccurrence.OccurrenceLabel);
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
