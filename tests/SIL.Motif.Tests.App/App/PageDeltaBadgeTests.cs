using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the delta badge each page keeps for the sidebar while a Proposal is open: Texts counts the words that moved
/// to Same, Warnings the findings gained or lost and Timing the percentage of parser work. A page that has not read its
/// side, cannot read it, or holds a side that covers other words shows none, and reading it never reads again.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class PageDeltaBadgeTests
{
    // An approved word the parser analyses as approved sits in Same; "no-analysis" is No Parse.
    private static AssessmentWordResult Word(string form, string outcome) => outcome == "analysed"
        ? new(form, outcome, false, "Search completed", 10, null)
        {
            ProjectStanding = ProjectStanding.Approved,
            ReadingGrades = ["approved"],
            Readings = [new ParserReading([new ParserReadingMorph(form, "gloss", "n", null, false, null)])],
            Morphology = new ParseWordEvidence("v1", 0, form, 10, false, false, false,
                [new ParseAnalysis([new ParseMorph(form + "-0", "n", null, null)])], []),
        }
        : new(form, outcome, false, "Complete", 1, null);

    // Same today is a alone; with the Proposal it is a, b and c, so two more words are Same.
    private static readonly AssessmentWordResult[] SameToday =
        [Word("a", "analysed"), Word("b", "no-analysis"), Word("c", "no-analysis"), Word("d", "unassessed")];

    private static readonly AssessmentWordResult[] SameWithProposal =
        [Word("a", "analysed"), Word("b", "analysed"), Word("c", "analysed"), Word("d", "unassessed")];

    private static Task<(WorkspaceContext Context, TextsPageModel Page, TextsProposalMatrixTests.Reads Reads)> TextsAsync(
        AssessmentWordResult[] proposal, bool onTexts = true, AssessmentWordResult[]? today = null,
        TextsProposalMatrixTests.Reads? reads = null)
    {
        reads ??= new TextsProposalMatrixTests.Reads();
        reads.Trials = _ => proposal;
        return TextsProposalMatrixTests.OpenedAsync(onTexts, today: today ?? SameToday, reads: reads);
    }

    [Fact]
    public async Task TextsCountsTheWordsThatMovedToSameWithItsSignAndSaysWhoseNumberItIs()
    {
        var (_, page, _) = await TextsAsync(SameWithProposal);

        var delta = Assert.IsType<PageDelta>(page.ProposalDelta);
        Assert.Equal("+2", delta.Text);
        Assert.Equal("+2", page.DeltaBadge);
        Assert.Contains("2 more words are Same with this Proposal", delta.AccessibleName, StringComparison.Ordinal);
        var entry = new PageViewModel(WorkspacePage.Texts, "Texts", "texts", page);
        Assert.True(entry.HasDeltaBadge);
        Assert.Equal("+2", entry.DeltaBadge);
        Assert.Equal(delta.AccessibleName, entry.DeltaBadgeName);
        Assert.Contains(delta.AccessibleName, entry.AutomationName, StringComparison.Ordinal);
        Assert.StartsWith("Texts page", entry.AutomationName, StringComparison.Ordinal);
        Assert.Equal("Texts: " + delta.AccessibleName, entry.RailTip);
        var none = new PageViewModel(WorkspacePage.Timing, "Timing", "timing", new TimingPageModel(page.Context));
        Assert.Equal("Timing", none.RailTip);
    }

    [Fact]
    public async Task TextsShowsAMinusSignWhenTheProposalLeavesFewerWordsInSame()
    {
        var (_, page, _) = await TextsAsync(SameToday, today: SameWithProposal);

        Assert.Equal("−2", page.DeltaBadge);
        Assert.Contains("2 fewer words are Same with this Proposal", page.ProposalDelta!.AccessibleName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TextsShowsNothingBeforeThePageHasReadItsSideAndThenShowsItOnceWithoutReadingAgain()
    {
        var reads = new TextsProposalMatrixTests.Reads();
        var (context, page, _) = await TextsAsync(SameWithProposal, onTexts: false, reads: reads);
        var entry = new PageViewModel(WorkspacePage.Texts, "Texts", "texts", page);
        Assert.Null(page.ProposalDelta);
        Assert.False(entry.HasDeltaBadge);

        context.OpenPage(WorkspacePage.Texts);
        await TextsProposalMatrixTests.UntilAsync(() => page.ProposalDelta is not null);
        var read = reads.Summaries;
        foreach (var view in new[] { MarkupView.Today, MarkupView.TodayToProposal, MarkupView.Proposal })
        {
            context.ShowProposalView(view);
            Assert.Equal("+2", page.DeltaBadge);
        }

        Assert.True(entry.HasDeltaBadge);
        Assert.Equal(read, reads.Summaries);
    }

    [Fact]
    public async Task TextsShowsNothingWhenNothingMovedToOrFromSame()
    {
        var (_, page, _) = await TextsAsync(SameToday);

        Assert.Null(page.ProposalDelta);
        Assert.Equal(string.Empty, page.DeltaBadge);
    }

    [Fact]
    public async Task TextsShowsNothingWhenTheProposalCoversOtherWordsOrCannotBeRead()
    {
        var (_, other, _) = await TextsAsync([.. SameWithProposal, Word("e", "analysed")]);
        Assert.False(other.Assess.Compare.ProposalWordsMatch);
        Assert.Null(other.ProposalDelta);

        var reads = new TextsProposalMatrixTests.Reads { Behave = _ => throw new InvalidOperationException("cannot read") };
        var (_, unreadable, _) = await TextsAsync(SameWithProposal, reads: reads);
        Assert.Null(unreadable.ProposalDelta);
    }

    [Fact]
    public async Task TextsLosesItWhenTheProposalIsLeft()
    {
        var (context, page, _) = await TextsAsync(SameWithProposal);
        Assert.NotNull(page.ProposalDelta);

        await context.LeaveProposalAsync();

        Assert.Null(page.ProposalDelta);
    }

    [Fact]
    public async Task WarningsCountsTheFindingsTheProposalGainsOrLosesAndSaysWhoseNumberItIs()
    {
        var (_, context, page) = await WarningsProposalViewTests.OpenedAsync(new GrammarCheckResponse(
            [WarningsProposalViewTests.Finding("b", GrammarDiagnosticLevel.Error)], true));
        await TextsProposalMatrixTests.UntilAsync(() => page.ProposalDelta is not null);
        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.Equal("−2", page.DeltaBadge);
        Assert.Contains("2 fewer warnings with this Proposal than today", page.ProposalDelta!.AccessibleName, StringComparison.Ordinal);
        var entry = new PageViewModel(WorkspacePage.Warnings, "Warnings", "warnings", page);
        Assert.Equal("−2", entry.DeltaBadge);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal("−2", page.DeltaBadge);
    }

    [Fact]
    public async Task WarningsShowsNothingForEqualCountsAMissingCheckOrAnUnreadableOne()
    {
        var (_, equal, equalPage) = await WarningsProposalViewTests.OpenedAsync(null);
        await Task.Delay(100);
        Assert.Null(equalPage.ProposalDelta);

        var (_, _, same) = await WarningsProposalViewTests.OpenedAsync(new GrammarCheckResponse(
        [
            WarningsProposalViewTests.Finding("x", GrammarDiagnosticLevel.Warning),
            WarningsProposalViewTests.Finding("y", GrammarDiagnosticLevel.Warning),
            WarningsProposalViewTests.Finding("z", GrammarDiagnosticLevel.Warning),
        ], true));
        await Task.Delay(100);
        Assert.Null(same.ProposalDelta);

        var (_, _, unreadable) = await WarningsProposalViewTests.OpenedAsync(null, unreadable: true);
        await Task.Delay(100);
        Assert.Null(unreadable.ProposalDelta);
    }

    [Fact]
    public async Task WarningsLosesItWhenTheProposalIsLeft()
    {
        var (_, context, page) = await WarningsProposalViewTests.OpenedAsync(new GrammarCheckResponse([], true));
        await TextsProposalMatrixTests.UntilAsync(() => page.ProposalDelta is not null);

        await context.LeaveProposalAsync();

        Assert.Null(page.ProposalDelta);
    }

    [Fact]
    public async Task TimingGivesTheChangeInParserWorkAsAWholePercentOverTheSameWords()
    {
        var (fake, context, page) = await TimingProposalViewTests.OpenedAsync();
        context.ShowProposalView(MarkupView.TodayToProposal);
        await TextsProposalMatrixTests.UntilAsync(() => page.ProposalDelta is not null);

        Assert.Equal("−10%", page.DeltaBadge);
        Assert.Contains("10% fewer parser work with this Proposal than today", page.ProposalDelta!.AccessibleName,
            StringComparison.Ordinal);
        var reads = TimingProposalViewTests.ProposalReads(fake);
        context.ShowProposalView(MarkupView.Proposal);
        context.ShowProposalView(MarkupView.Today);
        Assert.Equal("−10%", page.DeltaBadge);
        Assert.Equal(reads, TimingProposalViewTests.ProposalReads(fake));
    }

    [Fact]
    public async Task TimingShowsPlusWhenTheProposalCostsMore()
    {
        var side = new TimingProposalViewTests.Side
        {
            Proposals = _ => [new("a", 1_100), new("b", 2_200), new("c", 3_300), new("d", 4_400)],
        };
        var (_, context, page) = await TimingProposalViewTests.OpenedAsync(side);
        context.ShowProposalView(MarkupView.TodayToProposal);
        await TextsProposalMatrixTests.UntilAsync(() => page.ProposalDelta is not null);

        Assert.Equal("+10%", page.DeltaBadge);
    }

    [Fact]
    public async Task TimingShowsNothingWithoutAComparisonOverTheSameWords()
    {
        var (_, context, notComparable) = await TimingProposalViewTests.OpenedAsync(comparable: false);
        context.ShowProposalView(MarkupView.TodayToProposal);
        await Task.Delay(150);
        Assert.Null(notComparable.ProposalDelta);

        var other = new TimingProposalViewTests.Side { Proposals = _ => [new("a", 900), new("b", 1_800), new("c", 2_700), new("z", 3_600)] };
        var (_, otherContext, differentWords) = await TimingProposalViewTests.OpenedAsync(other);
        otherContext.ShowProposalView(MarkupView.TodayToProposal);
        await Task.Delay(150);
        Assert.Null(differentWords.ProposalDelta);

        var (_, _, unparsed) = await TimingProposalViewTests.OpenedAsync(todayParsed: false);
        await Task.Delay(150);
        Assert.Null(unparsed.ProposalDelta);
    }

    [Fact]
    public async Task TimingShowsNothingBeforeThePageHasReadItsSideOrAfterTheProposalIsLeft()
    {
        var (fake, context, page) = await TimingProposalViewTests.OpenedAsync(onTimingPage: false);
        context.ShowProposalView(MarkupView.TodayToProposal);
        await Task.Delay(100);
        Assert.Null(page.ProposalDelta);
        Assert.Equal(0, TimingProposalViewTests.ProposalReads(fake));

        context.OpenPage(WorkspacePage.Timing);
        await TextsProposalMatrixTests.UntilAsync(() => page.ProposalDelta is not null);
        await context.LeaveProposalAsync();

        Assert.Null(page.ProposalDelta);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    public void NoNumberAndNoChangeAreNoDeltaRatherThanZero(int? change, string? expected) =>
        Assert.Equal(expected, PageDelta.Of(change, PageDeltaKind.Warnings)?.Text);
}
