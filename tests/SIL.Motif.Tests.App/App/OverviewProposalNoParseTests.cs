using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the Overview when today has no parse but the open Proposal has one, and when neither does: which tiles show,
/// what today's side reads, and what the note says, in each of the three views.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class OverviewProposalNoParseTests
{
    private static async Task<(WorkspaceContext Context, OverviewPageModel Page)> OpenedAsync(
        OverviewResponse todayRead, OverviewResponse proposalRead)
    {
        var (_, context, page) = await OverviewProposalViewTests.OpenedAsync(
            OverviewProposalViewTests.Trial(), proposalRead, needsAssessment: true, today: todayRead);
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        return (context, page);
    }

    private static OverviewResponse TodayWithNoParse() => OverviewProposalViewTests.Today() with { AssessmentId = null };

    [Fact]
    public async Task TheTodayViewKeepsTodaysNoResultPageAndTheNoteStaysTrue()
    {
        var (context, page) = await OpenedAsync(TodayWithNoParse(), OverviewProposalViewTests.Proposal());
        Assert.True(context.NeedsAssessment);

        context.ShowProposalView(MarkupView.Today);

        Assert.False(page.ShowTiles);
        Assert.Equal(MotifAppStrings.OverviewTodayViewNote, page.ProposalNote);
    }

    [Fact]
    public async Task TodayToProposalShowsTheTilesWithTodaysSideReadingNoneAndSaysWhichHeadlinesHaveNoResultToday()
    {
        var (context, page) = await OpenedAsync(TodayWithNoParse(), OverviewProposalViewTests.Proposal());

        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.True(page.ShowTiles);
        Assert.Equal("Not parsed yet → 130 of 142 words parse", page.TextCoverageMain);
        Assert.Equal("Not parsed yet → 75 of 84 rebuilt exactly", page.AccuracyMain);
        Assert.Equal(MotifAppStrings.OverviewSpeedNoTimes + " → 142 words · 372,000 work steps in total", page.SpeedMain);
        Assert.Equal("20 warnings → 18 warnings", page.WarningsCount);
        Assert.Equal("The four headline numbers read today's value, then the value with this Proposal. " +
            "Everything else on this page is today's. 3 headlines have no result today: Text Coverage, " +
            "Approved Analyses Kept, Speed. Only the Proposal has a value for them.", page.ProposalNote);
    }

    [Fact]
    public async Task TheProposalViewShowsTheTilesWithTheProposalsValuesAlone()
    {
        var (context, page) = await OpenedAsync(TodayWithNoParse(), OverviewProposalViewTests.Proposal());

        context.ShowProposalView(MarkupView.Proposal);

        Assert.True(page.ShowTiles);
        Assert.Equal("130 of 142 words parse", page.TextCoverageMain);
        Assert.Equal(MotifAppStrings.OverviewProposalViewNote, page.ProposalNote);
    }

    [Theory]
    [InlineData(MarkupView.TodayToProposal)]
    [InlineData(MarkupView.Proposal)]
    public async Task WhenNeitherSideHasAParseNoParseTilesShowAndTheNoteSaysSo(MarkupView view)
    {
        var (context, page) = await OpenedAsync(TodayWithNoParse(),
            OverviewProposalViewTests.Proposal() with { AssessmentId = null });

        context.ShowProposalView(view);

        Assert.False(page.ShowTiles);
        Assert.Equal("Neither today nor this Proposal has a parse, so no parse numbers are shown.", page.ProposalNote);
    }

    [Fact]
    public async Task EachViewSaysWhetherTodaysBreakdownShowsUnderTheHeadlines()
    {
        var (context, page) = await OpenedAsync(OverviewProposalViewTests.Today(), OverviewProposalViewTests.Proposal());
        var raised = new List<string?>();
        page.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        foreach (var (view, detail) in new[]
            { (MarkupView.Today, true), (MarkupView.TodayToProposal, true), (MarkupView.Proposal, false) })
        {
            context.ShowProposalView(view);

            Assert.Equal(detail, page.ShowTodayDetail);
            Assert.Equal(!detail, page.HidesTodayDetail);
        }
        Assert.Contains(nameof(OverviewPageModel.ShowTodayDetail), raised);
        Assert.Contains(nameof(OverviewPageModel.HidesTodayDetail), raised);
    }
}
