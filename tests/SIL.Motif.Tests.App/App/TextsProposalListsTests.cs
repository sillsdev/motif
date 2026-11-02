using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the Texts Lists tab in Proposal mode: each list's counts as today's alone, today's with the Proposal's, or the
/// Proposal's alone, whose number each is, a value one side lacks never read as zero, and the buttons that send the
/// words listed.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextsProposalListsTests
{
    private static TextsListDefinitionViewModel NoParse(TextsPageModel page) => page.TextsLists.Lists.Single(list =>
        list.Cells.Contains(new TextsListCell(WordProjectStatus.NotPresent, CompareColumnKind.NoParse)));

    // Today's words a and b stand for three and two places in the texts, and c and d for one each.
    private static int? Places(string word) => word switch { "a" => 3, "b" => 2, _ => 1 };

    private static async Task<(WorkspaceContext Context, TextsPageModel Page, TextsListDefinitionViewModel List)> OnListsAsync(
        TextsProposalMatrixTests.Reads? reads = null)
    {
        var (context, page, _) = await TextsProposalMatrixTests.OpenedAsync(reads: reads);
        page.Assess.Compare.UpdateOccurrences(Places);
        page.Tab = TextsTab.Lists;
        return (context, page, NoParse(page));
    }

    [Fact]
    public async Task TheChipAndHeaderReadTodayToProposalInThePairAndTodaysAloneInTheTodayView()
    {
        var (context, page, list) = await OnListsAsync();
        context.ShowProposalView(MarkupView.Today);
        Assert.Equal("2", list.ChipCount);
        Assert.Equal("2 words · 5 places", list.CountText);

        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.Equal(TextsProposalMatrixTests.Pair("2", "1"), list.ChipCount);
        Assert.Equal(TextsProposalMatrixTests.Pair("2", "1 word") + " · " + TextsProposalMatrixTests.Pair("5", "3 places"),
            list.CountText);
        Assert.Equal(WordListBasis.Both, page.Assess.Compare.ListBasis);
    }

    [Fact]
    public async Task TheProposalViewReadsTheProposalsWordsAloneAndSaysItsPlacesAreNotCountedNeverZero()
    {
        var reads = new TextsProposalMatrixTests.Reads { Trials = _ => [.. TextsProposalMatrixTests.ProposalWords,
            TextsProposalMatrixTests.Word("e")] };
        var (context, _, list) = await OnListsAsync(reads);

        context.ShowProposalView(MarkupView.Proposal);

        Assert.Equal("2", list.ChipCount);
        Assert.Equal("2 words · " + MotifAppStrings.TextsListPlacesNotCounted, list.CountText);
    }

    [Fact]
    public async Task OverTheSameWordsTheProposalViewCountsThePlacesOfTheWordsItPutsOnTheList()
    {
        var (context, _, list) = await OnListsAsync();

        context.ShowProposalView(MarkupView.Proposal);

        Assert.Equal("1 word · 3 places", list.CountText);
    }

    [Fact]
    public async Task ADifferentWordSetKeepsTheListsOnTodaysCountsBecauseNothingPairs()
    {
        var reads = new TextsProposalMatrixTests.Reads { Trials = _ => [.. TextsProposalMatrixTests.ProposalWords,
            TextsProposalMatrixTests.Word("e")] };
        var (context, page, list) = await OnListsAsync(reads);

        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.Equal("2", list.ChipCount);
        Assert.StartsWith("2 words", list.CountText);
        Assert.Equal(MotifAppStrings.TextsListsDifferentWordsNote, page.ProposalNote);
        Assert.Equal(WordListBasis.Today, page.Assess.Compare.ListBasis);
    }

    [Fact]
    public async Task EachChipNamesWhoseNumberItReads()
    {
        var (context, _, list) = await OnListsAsync();
        context.ShowProposalView(MarkupView.Today);
        var today = list.AccessibleName;
        context.ShowProposalView(MarkupView.TodayToProposal);
        var pair = list.AccessibleName;
        context.ShowProposalView(MarkupView.Proposal);
        var proposal = list.AccessibleName;

        Assert.Equal(string.Format(MotifAppStrings.TextsListChipAccessibleNameFormat, list.Name), today);
        Assert.Equal(string.Format(MotifAppStrings.TextsListChipPairAccessibleNameFormat, list.Name, "2 words", "1 word"), pair);
        Assert.Equal(string.Format(MotifAppStrings.TextsListChipProposalAccessibleNameFormat, list.Name, "2 words", "1 word"),
            proposal);
        Assert.Contains("Proposal", proposal);
    }

    [Fact]
    public async Task AListTheProposalLeavesEmptyHidesItsChipUnlessItIsTheOneBeingRead()
    {
        var (context, page, _) = await OnListsAsync();
        var skipped = page.TextsLists.Lists.Single(list => list.Cells.Contains(
            new TextsListCell(WordProjectStatus.NotPresent, CompareColumnKind.Skipped)));
        var gone = page.TextsLists.Lists.First(list => list.Cells.Count == 1 && list.WordCount == 0 && !list.IsSelected);
        context.ShowProposalView(MarkupView.Proposal);

        Assert.True(skipped.ShowsChip);
        Assert.False(gone.ShowsChip);
        gone.IsSelected = true;
        Assert.True(gone.ShowsChip);
    }

    [Fact]
    public async Task TheWordsSentOnAreTheWordsListedWithTheProposalNotTodaysWholeCell()
    {
        var (context, page, list) = await OnListsAsync();
        page.TextsLists.SelectedList = list;
        string[]? sent = null;
        page.TextsLists.HandOff = words => sent = [.. words];
        context.ShowProposalView(MarkupView.Today);
        page.TextsLists.HandOffListCommand.Execute(null);
        Assert.Equal(["a", "b"], sent!.Order());

        context.ShowProposalView(MarkupView.Proposal);
        page.TextsLists.HandOffListCommand.Execute(null);

        Assert.Equal(["a"], sent!.Order());
    }

    [Fact]
    public async Task AFlipRereadsNothingAndTheListedLineSaysWhichWordsTheListShows()
    {
        var (context, page, list) = await OnListsAsync();
        page.TextsLists.SelectedList = list;
        var reads = page.Assess.Compare.ProposalWork;
        context.ShowProposalView(MarkupView.Today);
        Assert.False(page.TextsLists.ShowsListedLine);

        context.ShowProposalView(MarkupView.Proposal);

        Assert.True(page.TextsLists.ShowsListedLine);
        Assert.Equal(page.Assess.Compare.ListSummary, page.TextsLists.ListedLine);
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.Equal(reads, page.Assess.Compare.ProposalWork);
    }

    [Fact]
    public async Task ASideThatCannotBeReadReadsAsLackingAndNeverAsPlacesNotCountedOrADashInTheChipName()
    {
        var (context, page, list) = await OnListsAsync();
        page.TextsLists.SelectedList = list;
        foreach (var state in new[] { MatrixSideState.Unreadable, MatrixSideState.None })
        {
            page.Assess.Compare.ShowProposal(MarkupView.Proposal, state, null);

            Assert.DoesNotContain(MotifAppStrings.TextsListPlacesNotCounted, list.CountText);
            Assert.Contains(MissingValue.Marker, list.CountText);
            Assert.Equal(string.Format(MotifAppStrings.TextsListChipProposalNoneAccessibleNameFormat, list.Name), list.AccessibleName);
            Assert.DoesNotContain(MissingValue.Marker, list.AccessibleName);
        }
        GC.KeepAlive(context);
    }

    [Fact]
    public async Task TheHandOffButtonSaysWhoseWordsItSendsInEachView()
    {
        var (context, page, list) = await OnListsAsync();
        page.TextsLists.SelectedList = list;
        context.ShowProposalView(MarkupView.Today);
        Assert.Equal(string.Empty, page.TextsLists.HandOffListHelpText);

        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.Equal(MotifAppStrings.TextsListHandOffEitherSideHelp, page.TextsLists.HandOffListHelpText);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal(MotifAppStrings.TextsListHandOffProposalHelp, page.TextsLists.HandOffListHelpText);
    }
}
