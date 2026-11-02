using System.Diagnostics;
using System.Runtime.CompilerServices;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using Xunit.Abstractions;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the Moved By This Proposal chip on the Texts word list: it narrows the list to the words both sides have that
/// the Proposal puts in a different cell, counts them for the chosen cells, says whose number it is, hides when it
/// has nothing to list unless it is chosen, sends only the words listed, reads nothing for a flip and lets go with
/// the Proposal.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextsProposalMovedChipTests
{
    // Today a and b do not parse and c and d are not tried; with the Proposal b is not tried, so only b moves.
    private static string[] Listed(CompareViewModel compare) => [.. compare.Words.Select(word => word.Word).Order()];

    private static async Task<(WorkspaceContext Context, TextsPageModel Page, CompareViewModel Compare)> OpenedAsync(
        MarkupView view = MarkupView.TodayToProposal, TextsProposalMatrixTests.Reads? reads = null)
    {
        var (context, page, _) = await TextsProposalMatrixTests.OpenedAsync(reads: reads);
        context.ShowProposalView(view);
        return (context, page, page.Assess.Compare);
    }

    [Theory]
    [InlineData(MarkupView.TodayToProposal)]
    [InlineData(MarkupView.Proposal)]
    public async Task ChoosingTheChipListsOnlyTheWordsTheProposalMovesAndChoosingItAgainListsAllOfThemAgain(MarkupView view)
    {
        var (_, _, compare) = await OpenedAsync(view);
        Assert.Equal(1, compare.MovedChipCount);
        Assert.True(compare.ShowsMovedChip);
        Assert.False(compare.IsMovedFilterOn);
        var all = Listed(compare);

        compare.ToggleMovedFilterCommand.Execute(null);

        Assert.Equal(["b"], Listed(compare));
        Assert.True(compare.IsMovedFilterOn);
        Assert.Equal(MotifAppStrings.TextsMovedChipLabel, compare.ListHeading);
        Assert.Equal(string.Format(MotifAppStrings.TextsMovedListSummaryFormat, "1 of 4 words"), compare.ListSummary.Split(" . ")[0]);
        compare.ToggleMovedFilterCommand.Execute(null);
        Assert.Equal(all, Listed(compare));
    }

    [Fact]
    public async Task TheTodayViewHasNoChipListsEveryWordAndTheChoiceComesBackWithThePair()
    {
        var (context, _, compare) = await OpenedAsync();
        compare.ToggleMovedFilterCommand.Execute(null);

        context.ShowProposalView(MarkupView.Today);

        Assert.False(compare.ShowsMovedChip);
        Assert.False(compare.IsMovedFilterOn);
        Assert.Equal(["a", "b", "c", "d"], Listed(compare));
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.True(compare.IsMovedFilterOn);
        Assert.Equal(["b"], Listed(compare));
    }

    [Fact]
    public async Task TheCountIsOfTheChosenCellsAndTheChipHidesWhenItHasNothingUnlessItIsChosen()
    {
        var (_, page, compare) = await OpenedAsync();
        var matches = TextsProposalMatrixTests.Cell(page, CompareColumnKind.Match);
        var noParse = TextsProposalMatrixTests.Cell(page, CompareColumnKind.NoParse);

        compare.Toggle(matches, additive: false);
        Assert.Equal(0, compare.MovedChipCount);
        Assert.False(compare.ShowsMovedChip);

        compare.Toggle(noParse, additive: false);
        Assert.Equal(1, compare.MovedChipCount);
        Assert.True(compare.ShowsMovedChip);
        compare.ToggleMovedFilterCommand.Execute(null);
        compare.Toggle(matches, additive: false);

        Assert.Equal(0, compare.MovedChipCount);
        Assert.True(compare.ShowsMovedChip);
        Assert.Empty(Listed(compare));
        Assert.Equal(string.Format(MotifAppStrings.TextsMovedListSummaryFormat, "0 words"), compare.ListSummary.Split(" . ")[0]);
    }

    [Fact]
    public async Task OnlyWordsBothSidesHaveMoveSoAWordOnlyOneSideHasIsNeverCountedAsMoved()
    {
        var reads = new TextsProposalMatrixTests.Reads { Trials = _ => [TextsProposalMatrixTests.Word("a"),
            TextsProposalMatrixTests.Word("b", true), TextsProposalMatrixTests.Word("c", true),
            TextsProposalMatrixTests.Word("e"), TextsProposalMatrixTests.Word("f")] };
        var (_, _, compare) = await OpenedAsync(reads: reads);

        Assert.Equal(false, compare.ProposalWordsMatch);
        Assert.Equal(1, compare.MovedChipCount);
        compare.ToggleMovedFilterCommand.Execute(null);
        Assert.Equal(["b"], Listed(compare));
    }

    [Fact]
    public async Task TheChipNamesWhoseCellsTheWordsMovedBetweenAndTheCountSaysWords()
    {
        var (_, _, compare) = await OpenedAsync();

        Assert.Equal(string.Format(MotifAppStrings.TextsMovedChipAccessibleNameFormat, MotifAppStrings.TextsMovedChipLabel,
            "1 word that this Proposal puts in a different cell than today"), compare.MovedChipAccessibleName);
        Assert.Contains("this Proposal", compare.MovedChipAccessibleName);
        Assert.Contains("than today", compare.MovedChipAccessibleName);
    }

    [Fact]
    public async Task TheListsHandOffSendsOnlyTheWordsListedAndSaysSo()
    {
        var (context, page, compare) = await OpenedAsync();
        page.Tab = TextsTab.Lists;
        var noParse = page.TextsLists.Lists.Single(list => list.Cells.Contains(
            new TextsListCell(WordProjectStatus.NotPresent, CompareColumnKind.NoParse)));
        page.TextsLists.SelectedList = noParse;
        page.TextsLists.SelectListCommand.Execute(noParse);
        string[]? sent = null;
        page.TextsLists.HandOff = words => sent = [.. words];
        page.TextsLists.HandOffListCommand.Execute(null);
        Assert.Equal(["a", "b"], sent!.Order());

        compare.ToggleMovedFilterCommand.Execute(null);
        page.TextsLists.HandOffListCommand.Execute(null);

        Assert.Equal(["b"], sent!.Order());
        Assert.Equal(MotifAppStrings.TextsListHandOffMovedHelp, page.TextsLists.HandOffListHelpText);
        Assert.True(page.TextsLists.ShowsListedLine);
        Assert.Equal(compare.ListSummary, page.TextsLists.ListedLine);
        GC.KeepAlive(context);
    }

    [Fact]
    public async Task TheHandOffIsRefusedWhenTheListHasWordsButNoneOfThemMoves()
    {
        // The stopped word e is stopped with the Proposal too, so no list holding only it has a moved word.
        AssessmentWordResult Stopped(string form) => TextsProposalMatrixTests.Word(form) with { IsIncomplete = true };
        var reads = new TextsProposalMatrixTests.Reads { Trials = _ => [.. TextsProposalMatrixTests.ProposalWords, Stopped("e")] };
        var (context, page, _) = await TextsProposalMatrixTests.OpenedAsync(today: [.. TextsProposalMatrixTests.TodayWords, Stopped("e")],
            reads: reads);
        context.ShowProposalView(MarkupView.TodayToProposal);
        var compare = page.Assess.Compare;
        page.Tab = TextsTab.Lists;
        var stopped = page.TextsLists.Lists.Single(list => list.Cells.Contains(
            new TextsListCell(WordProjectStatus.NotPresent, CompareColumnKind.Timeout)));
        page.TextsLists.SelectListCommand.Execute(stopped);
        page.TextsLists.HandOff = _ => { };
        Assert.True(stopped.HasWords);
        Assert.True(page.TextsLists.HandOffListCommand.CanExecute(null));

        compare.ToggleMovedFilterCommand.Execute(null);

        Assert.True(compare.ShowsMovedChip);
        Assert.Equal(0, compare.MovedChipCount);
        Assert.False(page.TextsLists.HandOffListCommand.CanExecute(null));
        Assert.Equal(WindowCopy.NoWordsInListForHandoff, page.TextsLists.HandOffListDisabledReason);
    }

    [Fact]
    public async Task AFlipOrTheChoiceReadsNothingAndKeepsTheSortTheSearchAndTheCells()
    {
        var (context, page, compare) = await OpenedAsync();
        var noParse = TextsProposalMatrixTests.Cell(page, CompareColumnKind.NoParse);
        compare.Toggle(noParse, additive: false);
        compare.Sort = CompareSort.Alphabetical;
        compare.SearchText = "b";
        var summaries = (page.Assess.Compare.ProposalWork, compare.MoveSets);

        compare.ToggleMovedFilterCommand.Execute(null);
        foreach (var view in new[] { MarkupView.Proposal, MarkupView.Today, MarkupView.TodayToProposal })
            context.ShowProposalView(view);
        compare.ToggleMovedFilterCommand.Execute(null);

        Assert.Equal(summaries, (compare.ProposalWork, compare.MoveSets));
        Assert.True(noParse.IsSelected);
        Assert.Equal(CompareSort.Alphabetical, compare.Sort);
        Assert.Equal("b", compare.SearchText);
        Assert.Equal(["b"], Listed(compare));
    }

    [Fact]
    public async Task AFlipThatLeavesTheSameWordsListedDoesNotSortThemAgain()
    {
        var (context, _, compare) = await OpenedAsync(MarkupView.Proposal);
        compare.ToggleMovedFilterCommand.Execute(null);
        var runs = compare.FilterRuns;

        context.ShowProposalView(MarkupView.TodayToProposal);
        context.ShowProposalView(MarkupView.Proposal);

        Assert.Equal(runs + 2, compare.FilterRuns);
        Assert.Equal(["b"], Listed(compare));
    }

    [Fact]
    public async Task LeavingTheProposalDropsTheChoiceSoAnotherProposalStartsWithEveryWordListed()
    {
        var (context, page, compare) = await OpenedAsync();
        compare.ToggleMovedFilterCommand.Execute(null);

        await context.LeaveProposalAsync(CancellationToken.None);
        Assert.False(compare.IsMovedFilterOn);
        Assert.False(compare.ShowsMovedChip);
        Assert.Equal(["a", "b", "c", "d"], Listed(compare));

        await context.OpenProposalAsync("proposal/two", CancellationToken.None);
        await Wait.UntilAsync(() => compare.ProposalWordsMatch is not null);
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.False(compare.IsMovedFilterOn);
        Assert.Equal(["a", "b", "c", "d"], Listed(compare));
        Assert.True(compare.ShowsMovedChip);
        GC.KeepAlive(page);
    }
}

/// <summary>
/// Pins that the Moved By This Proposal chip over 688,000 words counts and filters from the cells already aligned,
/// aligns once, makes no word row to count and keeps nothing once the Proposal's side is dropped. It prints what the
/// choice costs, which depends on the machine and so is not asserted.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextsProposalMovedChipMemoryTests(ITestOutputHelper output)
{
    private const int Words = 688_000;

    // Every third word does not parse today and every fifth does not with the Proposal, so words move both ways.
    private static AssessmentWordResult[] Trial(int every) => [.. Enumerable.Range(0, Words).Select(index =>
        new AssessmentWordResult("word" + index, index % every == 0 ? "unassessed" : "no-analysis", false, "Complete", 1, null))];

    [MethodImpl(MethodImplOptions.NoInlining)]
    private (CompareViewModel Compare, long Baseline) Chosen()
    {
        var today = Trial(3);
        var summary = MatrixProposalSummary.Of(Trial(5));
        var compare = new CompareViewModel();
        compare.LoadSources(today);
        compare.ShowProposal(MarkupView.TodayToProposal, MatrixSideState.Held, summary);
        today = null!;
        GC.Collect();
        var baseline = GC.GetTotalMemory(true);
        var watch = Stopwatch.StartNew();

        var count = compare.MovedChipCount;
        var countMs = watch.ElapsedMilliseconds;
        var work = compare.ProposalWork;
        watch.Restart();
        compare.ToggleMovedFilterCommand.Execute(null);
        var chosenMs = watch.ElapsedMilliseconds;
        var held = GC.GetTotalMemory(true) - baseline;
        output.WriteLine($"MEASURE words={Words} moved={count} countMs={countMs} chooseMs={chosenMs} held={held}");

        var expected = Enumerable.Range(0, Words).Count(index => index % 3 == 0 != (index % 5 == 0));
        Assert.Equal(expected, count);
        Assert.Equal(count, compare.Words.Count);
        Assert.Equal(0, compare.MaterializedRowCount);
        compare.ToggleMovedFilterCommand.Execute(null);
        compare.ToggleMovedFilterCommand.Execute(null);
        Assert.Equal(work, compare.ProposalWork);
        Assert.Equal(count, compare.MovedChipCount);
        Assert.True(held < 40L << 20, $"choosing the chip held {held} bytes beyond today's words");
        return (compare, baseline);
    }

    [SlowScaleFact]
    [Trait("Category", "SlowScale")]
    public void ASixHundredEightyEightThousandWordProposalCountsAndFiltersItsMovedWordsWithoutRowsAndLetsAllOfThemGo()
    {
        var (compare, baseline) = Chosen();

        compare.ShowProposal(MarkupView.Today, MatrixSideState.None, null);
        for (var pass = 0; pass < 3; pass++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        var after = GC.GetTotalMemory(true) - baseline;
        output.WriteLine($"MEASURE words={Words} heldAfterRelease={after}");

        Assert.False(compare.IsMovedFilterOn);
        Assert.Equal(Words, compare.Words.Count);
        Assert.True(after <= 1L << 20, $"{after} bytes remain above today's own words");
        GC.KeepAlive(compare);
    }
}
