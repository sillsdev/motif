using SIL.Motif.App;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.App.TextsProposalMatrixTests;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the words listed under the Texts matrix in Proposal mode: today's in the Today view, today's or the
/// Proposal's in the pair, the Proposal's alone in the Proposal view, the With This Proposal column on each, a cell the
/// Proposal fills that can be chosen, and no digest or alignment made when no Proposal is open.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextsProposalWordListTests
{
    private static AssessmentWordResult Approved(string form, bool parsed = false, long? steps = null) =>
        Word(form, parsed) with { ProjectStanding = ProjectStanding.Approved, ParserWork = steps };

    private static string[] Listed(TextsPageModel page) => [.. page.Assess.Compare.Words.Select(word => word.Word).Order()];

    private static void Choose(TextsPageModel page, WordProjectStatus row, CompareColumnKind column)
    {
        page.Assess.Compare.SelectCells([new(row, column)]);
    }

    // Today: a and b fail to parse, c and d are not parsed; the Proposal parses a, skips b and c, fails d.
    [Fact]
    public async Task ThePairListsAWordIfEitherSidePutsItInTheChosenCellAndTheProposalViewListsTheProposalsAlone()
    {
        var (context, page, _) = await OpenedAsync();
        context.ShowProposalView(MarkupView.Today);

        Choose(page, WordProjectStatus.NotPresent, CompareColumnKind.NoParse);
        Assert.Equal(["a", "b"], Listed(page));
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.Equal(["a", "b"], Listed(page));
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal(["a"], Listed(page));

        Choose(page, WordProjectStatus.NotPresent, CompareColumnKind.Skipped);
        Assert.Equal(["b", "c", "d"], Listed(page));
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.Equal(["b", "c", "d"], Listed(page));
        context.ShowProposalView(MarkupView.Today);
        Assert.Equal(["c", "d"], Listed(page));
    }

    [Fact]
    public async Task ACellOnlyTheProposalFillsListsItsWordsInThePairAndTheProposalViewButNotInToday()
    {
        var reads = new Reads { Trials = _ => [Approved("a", true), Approved("b", true), Approved("c"), Approved("d")] };
        var (context, page, _) = await OpenedAsync(today: [Approved("a"), Approved("b"), Approved("c"), Approved("d")], reads: reads);
        context.ShowProposalView(MarkupView.Today);
        Choose(page, WordProjectStatus.Approved, CompareColumnKind.Skipped);

        Assert.Empty(Listed(page));
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.Equal(["a", "b"], Listed(page));
        Assert.Equal(string.Format(MotifAppStrings.CompareWordsEitherSideFormat, "2 words"), page.Assess.Compare.ListSummary);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal(["a", "b"], Listed(page));
        context.ShowProposalView(MarkupView.Today);
        Assert.Empty(Listed(page));
    }

    // The one cell no word of today's can reach is Not In FieldWorks and Same; this Proposal puts a and b there.
    private static MatrixProposalSummary FillsTheUnreachableCell(IReadOnlyList<AssessmentWordResult> words)
    {
        var real = MatrixProposalSummary.Of(words);
        var placed = words.Select(word => word.Word is "a" or "b"
            ? ProposalWordIndex.Pack(WordProjectStatus.NotPresent, CompareColumnKind.Match)
            : ProposalWordIndex.Pack(WordProjectStatus.NotPresent, CompareColumnKind.Skipped)).ToArray();
        return real with
        {
            Cells = new Dictionary<(WordProjectStatus, CompareColumnKind), int>
            {
                [(WordProjectStatus.NotPresent, CompareColumnKind.Match)] = 2,
                [(WordProjectStatus.NotPresent, CompareColumnKind.Skipped)] = 2,
            },
            Index = ProposalWordIndex.Of(words, placed),
        };
    }

    [Fact]
    public async Task ACellTheProposalFillsIsARealCellThatListsItsWordsInTheViewsThatDrawItsNumber()
    {
        var reads = new Reads { Behave = FillsTheUnreachableCell };
        var (context, page, _) = await OpenedAsync(reads: reads);
        var compare = page.Assess.Compare;
        var cell = Cell(page, CompareColumnKind.Match);
        context.ShowProposalView(MarkupView.Today);
        Assert.Equal(CompareFamilyKind.None, cell.Family);
        Assert.True(cell.IsEmptyImpossible);
        Assert.True(cell.IsNone);

        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.Equal(Pair("0", "2"), cell.CountNumberText);
        Assert.False(cell.IsEmptyImpossible);
        Assert.False(cell.IsNone);
        compare.SelectCells([new(WordProjectStatus.NotPresent, CompareColumnKind.Match)]);
        Assert.Equal(["a", "b"], Listed(page));

        context.ShowProposalView(MarkupView.Proposal);
        Assert.False(cell.IsEmptyImpossible);
        Assert.Equal(["a", "b"], Listed(page));
        context.ShowProposalView(MarkupView.Today);
        Assert.True(cell.IsEmptyImpossible);
        Assert.True(cell.IsNone);
    }

    [Fact]
    public async Task EveryListedWordSaysWhereTheProposalPutItAndAWordItDidNotMeasureSaysSoWithoutAMark()
    {
        var reads = new Reads { Trials = _ => [Word("a"), Word("b", true), Word("c", true)] };
        var (context, page, _) = await OpenedAsync(reads: reads);
        var compare = page.Assess.Compare;
        CompareWordViewModel Of(string word) => compare.Words.Single(listed => listed.Word == word);

        context.ShowProposalView(MarkupView.Today);
        Assert.All(compare.Words, word => Assert.Null(word.MatrixPresentation.WithProposal));
        Assert.Same(WordListSchemas.All, compare.WordListSchema);
        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.Equal(false, compare.ProposalWordsMatch);
        Assert.Same(WordListSchemas.AllWithProposal, compare.WordListSchema);
        Assert.Equal(["a", "b", "c", "d"], Listed(page));
        var parsed = Of("a").MatrixPresentation.WithProposal;
        Assert.NotNull(parsed);
        Assert.Equal(CompareViewModel.ColumnLabelOf(CompareColumnKind.NoParse), parsed.Text);
        Assert.NotNull(parsed.Mark);
        var missing = Of("d").MatrixPresentation.WithProposal;
        Assert.NotNull(missing);
        Assert.Equal(MotifAppStrings.TextsWordNotMeasured, missing.Text);
        Assert.Null(missing.Mark);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Same(WordListSchemas.WithProposalOnly, compare.WordListSchema);
        context.ShowProposalView(MarkupView.Today);
        Assert.All(compare.Words, word => Assert.Null(word.MatrixPresentation.WithProposal));
    }

    [Fact]
    public async Task TheProposalViewShowsNoWordsUntilItsSideIsReadWhileTodayAndThePairKeepTheirs()
    {
        var gate = new ManualResetEventSlim();
        var (context, page, reads) = await OpenedAsync(onTexts: false);
        reads.Behave = words =>
        {
            Assert.True(gate.Wait(TimeSpan.FromSeconds(20)));
            return MatrixProposalSummary.Of(words);
        };
        await context.OpenProposalAsync("proposal/two", CancellationToken.None);
        context.OpenPage(WorkspacePage.Texts);
        await Task.Delay(100);
        var compare = page.Assess.Compare;

        context.ShowProposalView(MarkupView.Proposal);
        Assert.False(compare.ShowsWordList);
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.True(compare.ShowsWordList);
        Assert.False(compare.ShowsProposalColumn);
        Assert.Same(WordListSchemas.All, compare.WordListSchema);

        gate.Set();
        await Wait.UntilAsync(() => page.ProposalNote == MotifAppStrings.TextsProposalPairedNote);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.True(compare.ShowsWordList);
        Assert.True(compare.ShowsProposalColumn);
    }

    [Fact]
    public async Task TheProposalViewOrdersByItsWorkButDoesNotCompareItWithUnpairedTodayValues()
    {
        var reads = new Reads { Trials = _ => [Approved("a", false, 90), Approved("b", false, 20)] };
        var (context, page, _) = await OpenedAsync(today: [Approved("a", false, 1), Approved("b", false, 50)], reads: reads);
        var compare = page.Assess.Compare;
        compare.Sort = CompareSort.Slowest;
        context.ShowProposalView(MarkupView.Today);

        Assert.Equal(["b", "a"], compare.Words.Select(word => word.Word));
        context.ShowProposalView(MarkupView.Proposal);

        Assert.Equal(["a", "b"], compare.Words.Select(word => word.Word));
        Assert.All(compare.Words, word => Assert.Equal("Parser work comparison unavailable", word.MatrixPresentation.TimeText));
        context.ShowProposalView(MarkupView.Today);
        Assert.Equal(["b", "a"], compare.Words.Select(word => word.Word));
    }

    [Fact]
    public async Task TheProposalViewSaysComparisonIsUnavailableWhenBaselineWorkWasNotRecorded()
    {
        var reads = new Reads { Trials = _ => [Approved("a", false, 90)] };
        var (context, page, _) = await OpenedAsync(today: [Approved("a")], reads: reads);

        context.ShowProposalView(MarkupView.Proposal);

        Assert.Equal("Parser work comparison unavailable", Assert.Single(page.Assess.Compare.Words).MatrixPresentation.TimeText);
    }

    [Fact]
    public async Task AFlipKeepsTheChosenCellTheSortAndTheSearchAcrossEveryListing()
    {
        var (context, page, reads) = await OpenedAsync();
        var compare = page.Assess.Compare;
        Choose(page, WordProjectStatus.NotPresent, CompareColumnKind.Skipped);
        compare.Sort = CompareSort.Alphabetical;
        compare.SearchText = "c";
        var summaries = reads.Summaries;

        foreach (var view in new[] { MarkupView.Proposal, MarkupView.TodayToProposal, MarkupView.Today })
        {
            context.ShowProposalView(view);
            Assert.Equal(["c"], compare.Words.Select(word => word.Word));
            Assert.Equal(CompareSort.Alphabetical, compare.Sort);
            Assert.Equal("c", compare.SearchText);
            Assert.Same(compare.Cells.Single(cell => cell.IsSelected), compare.ChosenCell);
        }
        Assert.Equal(summaries, reads.Summaries);
    }

    [Fact]
    public async Task NothingIsHashedOrAlignedForTheProposalWhenNoneIsOpenAndSomeIsOnceOneIs()
    {
        var (context, page, reads) = await OpenedAsync(open: false);
        var compare = page.Assess.Compare;

        Choose(page, WordProjectStatus.NotPresent, CompareColumnKind.NoParse);
        compare.SearchText = "a";
        compare.Sort = CompareSort.Slowest;
        compare.LoadSources([Word("p"), Word("q")]);
        context.ShowProposalView(MarkupView.TodayToProposal);
        context.ShowProposalView(MarkupView.Proposal);

        Assert.Null(compare.ProposalWordsMatch);
        Assert.Equal(WordListBasis.Today, compare.ListBasis);
        Assert.Equal(0, compare.ProposalWork);
        Assert.Equal(0, reads.Summaries);

        compare.LoadSources([Word("a"), Word("b"), Word("c", true), Word("d", true)]);
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        await Wait.UntilAsync(() => compare.ProposalWordsMatch is not null);
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.True(compare.ProposalWork > 0);
        Assert.Equal(1, reads.Summaries);
    }

    [Fact]
    public async Task TheListsAndWhatChangedNoteIsHiddenWhenThereIsNoMatrixToStandBesideIt()
    {
        var (context, page, _) = await OpenedAsync();
        context.ShowProposalView(MarkupView.TodayToProposal);
        page.Tab = TextsTab.WhatChanged;
        Assert.Equal(MotifAppStrings.TextsWhatChangedPairedNote, page.ProposalNote);

        context.Assess.Restore(new WorkspaceEvidence(TimingProposalViewTests.Assessment() with { Words = [] },
            DateTimeOffset.UtcNow, false));

        Assert.Null(page.ProposalNote);
        Assert.False(page.HasProposalNote);
        page.Tab = TextsTab.Lists;
        Assert.Null(page.ProposalNote);
    }

    [Fact]
    public async Task TheTodayViewKeepsItsNoteWhileTheProposalSideIsBeingRead()
    {
        var gate = new ManualResetEventSlim();
        var (context, page, reads) = await OpenedAsync(onTexts: false);
        reads.Behave = words =>
        {
            Assert.True(gate.Wait(TimeSpan.FromSeconds(20)));
            return MatrixProposalSummary.Of(words);
        };
        await context.OpenProposalAsync("proposal/two", CancellationToken.None);
        context.OpenPage(WorkspacePage.Texts);
        await Task.Delay(100);

        context.ShowProposalView(MarkupView.Today);
        var reading = page.ProposalNote;
        context.ShowProposalView(MarkupView.Proposal);
        var proposal = page.ProposalNote;
        gate.Set();
        await Wait.UntilAsync(() => page.Assess.Compare.ProposalWordsMatch is not null);

        Assert.NotNull(reading);
        Assert.Contains(MotifAppStrings.TextsMatrixCountsNoun, reading);
        Assert.Null(proposal);
    }

    [Fact]
    public void TheNotesNoLongerSayTheWordListIsUnpairedButStillSayListsAndWhatChangedAre()
    {
        foreach (var note in new[] { MotifAppStrings.TextsProposalPairedNote, MotifAppStrings.TextsProposalDifferentWordsNote,
            MotifAppStrings.TextsProposalViewNote })
        {
            Assert.DoesNotContain("word list", note, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("What Changed", note);
        }
    }

    // Today's a, b, c and d occur 2, 3, 5 and 7 times; the Proposal puts a in No Parse and b, c and d in Skipped.
    private static readonly Dictionary<string, int> Places = new() { ["a"] = 2, ["b"] = 3, ["c"] = 5, ["d"] = 7 };

    private static string PlacesOf(int count) => CompareCellViewModel.PlacesTextOf(count);

    private static CompareViewModel Counted(AssessmentWordResult[] trial, MarkupView view)
    {
        var compare = new CompareViewModel();
        compare.LoadSources(TodayWords, word => Places[word]);
        compare.ShowProposal(view, MatrixSideState.Held, MatrixProposalSummary.Of(trial));
        return compare;
    }

    [Fact]
    public void ThePlacesUnderACellAreTheProposalsInItsViewAndTodaysThenTheProposalsInThePair()
    {
        var compare = Counted(ProposalWords, MarkupView.Today);
        var noParse = CellOf(compare, CompareColumnKind.NoParse);
        var skipped = CellOf(compare, CompareColumnKind.Skipped);
        Assert.Equal([PlacesOf(5), PlacesOf(12)], [noParse.PlacesText, skipped.PlacesText]);

        compare.ShowProposal(MarkupView.TodayToProposal, MatrixSideState.Held, MatrixProposalSummary.Of(ProposalWords));
        Assert.Equal([Pair("5", PlacesOf(2)), Pair("12", PlacesOf(15))],
            [noParse.PlacesText, skipped.PlacesText]);

        compare.ShowProposal(MarkupView.Proposal, MatrixSideState.Held, MatrixProposalSummary.Of(ProposalWords));
        Assert.Equal([PlacesOf(2), PlacesOf(15)], [noParse.PlacesText, skipped.PlacesText]);
        Assert.EndsWith(", " + PlacesOf(15) + ".", skipped.AccessibleName);
        Assert.Equal(PluralText.Format(MotifAppStrings.CompareWordUnitPattern, 1), noParse.CountUnitText);
    }

    [Fact]
    public void APlaceCountTheTrialsExtraWordsWouldChangeIsNeverShownAsCompleteOrAsZero()
    {
        AssessmentWordResult[] trial = [.. ProposalWords, Word("z", true)];
        var compare = Counted(trial, MarkupView.Proposal);
        var noParse = CellOf(compare, CompareColumnKind.NoParse);
        var skipped = CellOf(compare, CompareColumnKind.Skipped);

        Assert.Equal(PlacesOf(2), noParse.PlacesText);
        Assert.Equal(string.Empty, skipped.PlacesText);
        Assert.Equal(MotifAppStrings.TextsListPlacesNotCounted, skipped.PlacesNote);
        Assert.True(skipped.ShowsPlaces);
        Assert.Equal("4", skipped.CountNumberText);

        compare.ShowProposal(MarkupView.TodayToProposal, MatrixSideState.Held, MatrixProposalSummary.Of(trial));
        Assert.Equal(PlacesOf(12), skipped.PlacesText);
    }

    [Fact]
    public void TheHeadingSaysHowManyWordsTheCellCountsButCannotListAndEveryOtherNumberStaysTrue()
    {
        AssessmentWordResult[] trial = [.. ProposalWords, Word("z", true)];
        var compare = Counted(trial, MarkupView.Proposal);
        var notListed = PluralText.Format(MotifAppStrings.CompareWordsNotListedPattern, 1);
        compare.SelectCells([new(WordProjectStatus.NotPresent, CompareColumnKind.Skipped)]);

        Assert.Equal(["b", "c", "d"], compare.Words.Select(word => word.Word).Order());
        Assert.Equal(string.Format(MotifAppStrings.CompareWordsAndPlacesFormat,
            string.Format(MotifAppStrings.CompareWordsAndPlacesFormat, "3 words", PlacesOf(15)), notListed), compare.ListSummary);
        compare.SearchText = "b";
        Assert.Contains("1 of 3 words", compare.ListSummary);
        Assert.EndsWith(notListed, compare.ListSummary);
        compare.SearchText = "";
        compare.SelectCells([]);
        Assert.Equal(4, compare.Words.Count);
        Assert.StartsWith("4 words", compare.ListSummary);
        Assert.EndsWith(notListed, compare.ListSummary);

        compare.ShowProposal(MarkupView.Today, MatrixSideState.Held, MatrixProposalSummary.Of(trial));
        Assert.DoesNotContain("counted but not listed", compare.ListSummary);
        compare.ShowProposal(MarkupView.Proposal, MatrixSideState.Held, MatrixProposalSummary.Of(ProposalWords));
        Assert.DoesNotContain("counted but not listed", compare.ListSummary);
    }

    [Fact]
    public void TheProposalViewNoteSaysAWordOnlyTheTrialMeasuredIsCountedButNotListed()
    {
        Assert.Contains("counted but not listed", MotifAppStrings.TextsProposalViewNote);
    }

    private static CompareCellViewModel CellOf(CompareViewModel compare, CompareColumnKind column) =>
        compare.Cells.Single(cell => cell.Row == WordProjectStatus.NotPresent && cell.Column == column);

    [Fact]
    public async Task AnOpenWordCardStaysWhileItsWordStaysListedAndClosesWhenTheProposalDoesNotPlaceItInTheChosenCell()
    {
        var (context, page, _) = await OpenedAsync();
        var compare = page.Assess.Compare;
        context.ShowProposalView(MarkupView.Today);
        Choose(page, WordProjectStatus.NotPresent, CompareColumnKind.NoParse);
        CompareWordViewModel Of(string word) => compare.Words.Single(listed => listed.Word == word);

        var kept = Of("a");
        kept.IsExpanded = true;
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Same(kept, Of("a"));
        Assert.True(kept.IsExpanded);

        context.ShowProposalView(MarkupView.Today);
        Of("b").IsExpanded = true;
        Assert.False(kept.IsExpanded);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal(["a"], Listed(page));
        context.ShowProposalView(MarkupView.Today);

        Assert.False(Of("b").IsExpanded);
        Assert.DoesNotContain(compare.Words, word => word.IsExpanded);
    }

    private static (WeakReference Summary, Reads Reads) Watching()
    {
        var weak = new WeakReference(null);
        var reads = new Reads();
        reads.Behave = words =>
        {
            var summary = MatrixProposalSummary.Of(words);
            weak.Target = summary;
            return summary;
        };
        return (weak, reads);
    }

    private static async Task<bool> LivesAsync(WeakReference weak)
    {
        for (var attempt = 0; attempt < 5 && weak.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(20);
        }
        return weak.IsAlive;
    }

    [Fact]
    public async Task LeavingTheProposalLetsGoOfTheTrialsSideSoItsWordsAreNotKeptAlive()
    {
        var (weak, reads) = Watching();
        var (context, _, _) = await OpenedAsync(reads: reads);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.True(weak.IsAlive);

        await context.LeaveProposalAsync(CancellationToken.None);

        Assert.False(await LivesAsync(weak));
    }

    [Fact]
    public async Task OpeningAnotherProposalLetsGoOfTheEarlierOnesSideEvenWhileTextsIsNotShown()
    {
        var (weak, reads) = Watching();
        var (context, _, _) = await OpenedAsync(reads: reads);
        Assert.True(weak.IsAlive);
        context.OpenPage(WorkspacePage.Overview);

        await context.OpenProposalAsync("proposal/two", CancellationToken.None);

        Assert.Equal(1, reads.Summaries);
        Assert.False(await LivesAsync(weak));
    }

    [Fact]
    public async Task AProposalBeingReadAfterAnotherDoesNotKeepTheEarlierOnesSideAlive()
    {
        var (weak, reads) = Watching();
        var (context, _, _) = await OpenedAsync(reads: reads);
        var gate = new ManualResetEventSlim();
        reads.Behave = words =>
        {
            gate.Wait(TimeSpan.FromSeconds(20));
            return MatrixProposalSummary.Of(words);
        };
        context.OpenPage(WorkspacePage.Overview);
        await context.OpenProposalAsync("proposal/two", CancellationToken.None);

        context.OpenPage(WorkspacePage.Texts);
        await Wait.UntilAsync(() => reads.Summaries == 2);
        var alive = await LivesAsync(weak);
        gate.Set();

        Assert.False(alive);
    }

    [Fact]
    public void ACellsAccessibleNameSaysWordsAndPlacesForTheNumbersItReadsWithTheProposal()
    {
        var compare = Counted(ProposalWords, MarkupView.TodayToProposal);
        var skipped = CellOf(compare, CompareColumnKind.Skipped);
        Assert.EndsWith($"reads {Pair("2", "3")} words, {Pair("12", PlacesOf(15))}.", skipped.AccessibleName);

        compare.ShowProposal(MarkupView.Proposal, MatrixSideState.Held, MatrixProposalSummary.Of(ProposalWords));
        Assert.EndsWith($"reads 3 words, {PlacesOf(15)}.", skipped.AccessibleName);
    }

    [Fact]
    public void ThePairHeadingSaysAChosenCellListsTheWordsOfEitherSide()
    {
        var compare = Counted(ProposalWords, MarkupView.TodayToProposal);
        compare.SelectCells([new(WordProjectStatus.NotPresent, CompareColumnKind.NoParse)]);
        var either = string.Format(MotifAppStrings.CompareWordsEitherSideFormat, "2 words");

        Assert.StartsWith(either, compare.ListSummary);
        compare.ShowProposal(MarkupView.Today, MatrixSideState.Held, MatrixProposalSummary.Of(ProposalWords));
        Assert.DoesNotContain("with this Proposal", compare.ListSummary);
        compare.ShowProposal(MarkupView.Proposal, MatrixSideState.Held, MatrixProposalSummary.Of(ProposalWords));
        Assert.DoesNotContain("with this Proposal", compare.ListSummary);
    }

    [Fact]
    public void AFlipBetweenTodayAndThePairWithNoCellChosenFiltersAndSortsNothingAgain()
    {
        var compare = Counted(ProposalWords, MarkupView.Today);
        var summary = MatrixProposalSummary.Of(ProposalWords);
        var runs = compare.FilterRuns;

        compare.ShowProposal(MarkupView.TodayToProposal, MatrixSideState.Held, summary);
        compare.ShowProposal(MarkupView.Today, MatrixSideState.Held, summary);

        Assert.Equal(runs, compare.FilterRuns);
        compare.SelectCells([new(WordProjectStatus.NotPresent, CompareColumnKind.NoParse)]);
        compare.ShowProposal(MarkupView.TodayToProposal, MatrixSideState.Held, summary);
        Assert.True(compare.FilterRuns > runs);
    }
}
