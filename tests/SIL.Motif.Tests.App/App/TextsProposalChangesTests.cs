using System.Runtime.CompilerServices;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the Texts What Changed tab in Proposal mode: in the pair it lists the words that went from one cell today to
/// another with the open Proposal, over the words both sides have, counted once for a Proposal and a load, made only
/// while the tab is shown in the pair, and let go of when the Proposal is left or the evidence is replaced.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextsProposalChangesTests
{
    private static async Task<(WorkspaceContext Context, TextsPageModel Page)> OnWhatChangedAsync(
        TextsProposalMatrixTests.Reads? reads = null, MarkupView view = MarkupView.TodayToProposal)
    {
        var (context, page, _) = await TextsProposalMatrixTests.OpenedAsync(reads: reads);
        context.ShowProposalView(view);
        page.Tab = TextsTab.WhatChanged;
        return (context, page);
    }

    [Fact]
    public async Task ThePairListsTheWordsThatChangedCellFromTodaysCellToTheProposalsAndCountsTheRest()
    {
        var (_, page) = await OnWhatChangedAsync();
        var changes = page.ProposalChanges;

        Assert.True(page.ShowProposalMovesContent);
        Assert.False(page.ShowWhatChangedContent);
        Assert.Equal(4, changes.ComparedCount);
        Assert.Equal(1, changes.MovedCount);
        var move = Assert.Single(changes.Moves);
        Assert.True(move.ToProposal);
        Assert.Equal((WordProjectStatus.NotPresent, CompareColumnKind.NoParse), move.From);
        Assert.Equal((WordProjectStatus.NotPresent, CompareColumnKind.Skipped), move.To);
        Assert.Equal(["b"], move.Words.Select(word => word.Word));
        Assert.Equal(string.Empty, changes.OnlyInOneText);
    }

    [Fact]
    public async Task OnlyTheWordsBothSidesHaveAreComparedAndTheOthersAreCountedNeverMovedOrZeroed()
    {
        var reads = new TextsProposalMatrixTests.Reads { Trials = _ => [TextsProposalMatrixTests.Word("a"),
            TextsProposalMatrixTests.Word("b", true), TextsProposalMatrixTests.Word("c", true),
            TextsProposalMatrixTests.Word("e"), TextsProposalMatrixTests.Word("f")] };
        var (_, page) = await OnWhatChangedAsync(reads);
        var changes = page.ProposalChanges;

        Assert.False(page.Assess.Compare.ProposalWordsMatch);
        Assert.True(page.ShowProposalMovesContent);
        Assert.Equal(3, changes.ComparedCount);
        Assert.Equal(1, changes.MovedCount);
        Assert.Equal(string.Format(MotifAppStrings.DifferenceProposalOnlyInOneFormat, "1", "2"), changes.OnlyInOneText);
        Assert.DoesNotContain(changes.Moves.SelectMany(move => move.Words), word => word.Word is "d" or "e" or "f");
    }

    [Fact]
    public async Task TheMoveAndItsWordsSayWhichCellIsTodaysAndWhichIsWithThisProposal()
    {
        var (_, page) = await OnWhatChangedAsync();
        var move = page.ProposalChanges.Moves.Single();
        var word = move.Words.Single();

        Assert.Contains("Today", move.TransitionText);
        Assert.Contains("with this Proposal", move.TransitionText);
        Assert.Equal(MotifAppStrings.DifferenceProposalFromLabel, page.ProposalChanges.BeforeLabel);
        Assert.Equal(MotifAppStrings.DifferenceProposalToLabel, page.ProposalChanges.AfterLabel);
        Assert.StartsWith(string.Format(MotifAppStrings.DifferenceWithProposalFormat, string.Empty).TrimEnd(), word.BeforeNote);
        Assert.True(word.IsProposalMove);
    }

    [Fact]
    public async Task AFlipOrASecondLookMakesNoSecondSetAndLeavingTheProposalLetsTheMovesGo()
    {
        var (context, page) = await OnWhatChangedAsync();
        var compare = page.Assess.Compare;
        Assert.Equal(1, compare.MoveSets);
        var held = page.ProposalChanges.ProposalMoves;

        context.ShowProposalView(MarkupView.Today);
        context.ShowProposalView(MarkupView.TodayToProposal);
        context.ShowProposalView(MarkupView.Proposal);
        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.Equal(1, compare.MoveSets);
        Assert.Same(held, page.ProposalChanges.ProposalMoves);
        await context.LeaveProposalAsync(CancellationToken.None);
        Assert.Null(compare.HeldProposalMoves);
        Assert.Null(page.ProposalChanges.ProposalMoves);
        Assert.Empty(page.ProposalChanges.Moves);
        Assert.True(page.ShowWhatChangedContent);
    }

    [Fact]
    public async Task NoMovesAreMadeUntilWhatChangedIsShownInThePair()
    {
        var (context, page, _) = await TextsProposalMatrixTests.OpenedAsync();
        context.ShowProposalView(MarkupView.TodayToProposal);
        page.Tab = TextsTab.Lists;
        var compare = page.Assess.Compare;

        Assert.Equal(0, compare.MoveSets);
        Assert.Null(page.ProposalChanges.ProposalMoves);
        page.Tab = TextsTab.WhatChanged;
        Assert.Equal(1, compare.MoveSets);
        Assert.NotNull(page.ProposalChanges.ProposalMoves);
    }

    [Fact]
    public async Task AnotherProposalReplacesTheMovesAndNewEvidenceRemakesThemOverTheNewWords()
    {
        var (context, page) = await OnWhatChangedAsync();
        var first = page.ProposalChanges.ProposalMoves;
        await context.OpenProposalAsync("proposal/two", CancellationToken.None);
        await Wait.UntilAsync(() => page.Assess.Compare.ProposalWordsMatch is not null &&
            page.ProposalChanges.ProposalMoves is not null && !ReferenceEquals(page.ProposalChanges.ProposalMoves, first));

        Assert.NotSame(first, page.ProposalChanges.ProposalMoves);
        var second = page.ProposalChanges.ProposalMoves;
        var evidence = new WorkspaceEvidence(TimingProposalViewTests.Assessment() with
        {
            Words = [.. TextsProposalMatrixTests.TodayWords, TextsProposalMatrixTests.Word("z")],
        }, DateTimeOffset.UtcNow, false);
        context.Assess.Restore(evidence);
        context.PublishEvidence(evidence);
        await context.EvidencePublication;

        Assert.NotSame(second, page.ProposalChanges.ProposalMoves);
        Assert.Equal(string.Format(MotifAppStrings.DifferenceProposalOnlyInOneFormat, "1", "0"), page.ProposalChanges.OnlyInOneText);
    }

    [Fact]
    public async Task TheTodayViewKeepsTheTwoRunsComparisonAndTheProposalViewListsTheSameMovesAsThePair()
    {
        var (context, page) = await OnWhatChangedAsync(view: MarkupView.Today);

        Assert.True(page.ShowWhatChangedContent);
        Assert.False(page.ShowProposalMovesContent);
        Assert.Null(page.Assess.Compare.HeldProposalMoves);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.False(page.ShowWhatChangedContent);
        Assert.True(page.ShowProposalMovesContent);
        Assert.Equal(MotifAppStrings.TextsWhatChangedViewNote, page.ProposalNote);
        var inProposal = page.ProposalChanges.ProposalMoves;
        var move = Assert.Single(page.ProposalChanges.Moves);
        Assert.Equal(["b"], move.Words.Select(word => word.Word));
        Assert.Equal(1, page.Assess.Compare.MoveSets);

        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.True(page.ShowProposalMovesContent);
        Assert.Same(inProposal, page.ProposalChanges.ProposalMoves);
        Assert.Equal(1, page.Assess.Compare.MoveSets);
        await context.LeaveProposalAsync(CancellationToken.None);
        Assert.Null(page.Assess.Compare.HeldProposalMoves);
    }

    [Fact]
    public async Task TheMatrixNoteInTheProposalViewSaysWhatChangedListsTheMoves()
    {
        var (_, page, _) = await TextsProposalMatrixTests.OpenedAsync();
        page.Tab = TextsTab.Matrix;
        page.Context.ShowProposalView(MarkupView.Proposal);

        Assert.Equal(MotifAppStrings.TextsProposalViewNote, page.ProposalNote);
        Assert.Contains("What Changed lists the words the Proposal moves", page.ProposalNote, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing to show", page.ProposalNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheProposalViewWithoutItsSideListsNoMovesAndMakesNone()
    {
        var reads = new TextsProposalMatrixTests.Reads { Behave = _ => throw new InvalidOperationException("cannot read") };
        var (_, page) = await OnWhatChangedAsync(reads, MarkupView.Proposal);

        Assert.Null(page.Assess.Compare.ProposalWordsMatch);
        Assert.False(page.ShowProposalMovesContent);
        Assert.False(page.ShowWhatChangedContent);
        Assert.Equal(0, page.Assess.Compare.MoveSets);
        Assert.Null(page.ProposalChanges.ProposalMoves);
    }

    [Fact]
    public async Task AProposalThatMovesNothingSaysSoAndIsNotCalledEmpty()
    {
        var reads = new TextsProposalMatrixTests.Reads { Trials = _ => TextsProposalMatrixTests.TodayWords };
        var (_, page) = await OnWhatChangedAsync(reads);

        Assert.Empty(page.ProposalChanges.Moves);
        Assert.Equal(4, page.ProposalChanges.ComparedCount);
        Assert.Equal(0, page.ProposalChanges.MovedCount);
        Assert.True(page.ProposalChanges.HasDifference);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference Moves, WeakReference Side)> OpenAndLeaveAsync(WorkspaceContext context, TextsPageModel page)
    {
        var moves = page.Assess.Compare.HeldProposalMoves!;
        var references = (new WeakReference(moves), new WeakReference(moves.Summary));
        await context.LeaveProposalAsync(CancellationToken.None);
        return references;
    }

    [Fact]
    public async Task LeavingTheProposalLeavesNeitherTheMovesNorTheSideTheyWereFoundAgainstReachable()
    {
        var (context, page) = await OnWhatChangedAsync();

        var (moves, side) = await OpenAndLeaveAsync(context, page);
        for (var pass = 0; pass < 3; pass++) { GC.Collect(); GC.WaitForPendingFinalizers(); }

        Assert.False(moves.IsAlive, "the moves are still held after the Proposal was left");
        Assert.False(side.IsAlive, "the Proposal's side is still held after the Proposal was left");
        GC.KeepAlive(page);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (CompareViewModel Compare, WeakReference Moves) MovesOfASideThenDropIt()
    {
        var compare = new CompareViewModel();
        compare.LoadSources(TextsProposalMatrixTests.TodayWords);
        compare.ShowProposal(MarkupView.TodayToProposal, MatrixSideState.Held,
            MatrixProposalSummary.Of(TextsProposalMatrixTests.ProposalWords));
        var moves = new WeakReference(compare.ProposalMoves());
        compare.ShowProposal(MarkupView.Today, MatrixSideState.None, null);
        return (compare, moves);
    }

    [Fact]
    public void DroppingTheSideWithoutAReloadLeavesTheMovesUnreachable()
    {
        var (compare, moves) = MovesOfASideThenDropIt();
        for (var pass = 0; pass < 3; pass++) { GC.Collect(); GC.WaitForPendingFinalizers(); }

        Assert.False(moves.IsAlive, "the moves outlived the Proposal's side");
        Assert.Null(compare.HeldProposalMoves);
        GC.KeepAlive(compare);
    }

    [Fact]
    public async Task AMovesWordsAreMadeWhenReadAndListedMostPlacesFirst()
    {
        var today = new[] { "a", "b", "c", "d" }.Select(form => TextsProposalMatrixTests.Word(form)).ToArray();
        var reads = new TextsProposalMatrixTests.Reads { Trials = _ => [.. today.Select(word =>
            TextsProposalMatrixTests.Word(word.Word, true))] };
        var (context, page, _) = await TextsProposalMatrixTests.OpenedAsync(today: today, reads: reads);
        page.Assess.Compare.UpdateOccurrences(word => word switch { "a" => 1, "b" => 5, "c" => 5, _ => 2 });
        context.ShowProposalView(MarkupView.TodayToProposal);
        page.Tab = TextsTab.WhatChanged;
        var words = Assert.IsType<LazyMovedWords>(Assert.Single(page.ProposalChanges.Moves).Words);

        Assert.Equal(4, words.Count);
        Assert.Equal(0, words.Made);
        Assert.Equal("b", words[0].Word);
        Assert.Equal(1, words.Made);
        Assert.Same(words[0], words[0]);
        Assert.Equal(["b", "c", "d", "a"], words.Select(word => word.Word));
    }

    [Theory]
    [InlineData("a,a,b", "a,b,c", 1)]
    [InlineData("a,a", "a", 0)]
    [InlineData("a,b", "a,a,c,c", 1)]
    public void TheWordsOnlyTheProposalMeasuredAreCountedFromTheWordsNotFromTheTotals(string today, string proposal, int only)
    {
        var compare = new CompareViewModel();
        compare.LoadSources([.. today.Split(',').Select(form => TextsProposalMatrixTests.Word(form))]);
        compare.ShowProposal(MarkupView.TodayToProposal, MatrixSideState.Held, MatrixProposalSummary.Of(
            [.. proposal.Split(',').Select(form => TextsProposalMatrixTests.Word(form))]));

        Assert.Equal(only, compare.ProposalMoves()!.ProposalOnly);
    }

    [Fact]
    public async Task NothingIsBuiltWhileAnotherPageIsShownAndTheMovesComeWhenTextsIsShownAgain()
    {
        var (context, page, _) = await TextsProposalMatrixTests.OpenedAsync();
        context.ShowProposalView(MarkupView.Today);
        page.Tab = TextsTab.WhatChanged;
        context.OpenPage(WorkspacePage.Overview);

        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.Equal(0, page.Assess.Compare.MoveSets);
        Assert.Null(page.ProposalChanges.ProposalMoves);
        context.OpenPage(WorkspacePage.Texts);
        Assert.True(page.Assess.Compare.MoveSets >= 1);
        Assert.NotNull(page.ProposalChanges.ProposalMoves);
    }
}
