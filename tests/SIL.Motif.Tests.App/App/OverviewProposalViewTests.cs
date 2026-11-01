using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Baselines;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that the Overview draws the view chosen on the Proposal bar from the pair it already holds, never reads
/// again for a switch, and never shows today's number as the Proposal's.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class OverviewProposalViewTests
{
    private static readonly string ProjectPath = Path.Combine(Path.GetTempPath(), "sample.fwdata");

    private static readonly BaselineToken Token = new(
        "project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));

    [Fact]
    public async Task TheTodayViewShowsTodaysHeadlinesAloneAndSaysHowToSeeTheProposals()
    {
        var (fake, context, page) = await OpenedAsync(Trial());
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        var reads = fake.OverviewRequests.Count;

        context.ShowProposalView(MarkupView.Today);

        Assert.Equal("118 of 142 words parse", page.TextCoverageMain);
        Assert.Equal("71 of 84 rebuilt exactly", page.AccuracyMain);
        Assert.Equal("142 words · 380,000 work steps in total", page.SpeedMain);
        Assert.Equal("20 warnings", page.WarningsCount);
        Assert.Equal(MotifAppStrings.OverviewTodayViewNote, page.ProposalNote);
        Assert.Equal(reads, fake.OverviewRequests.Count);
    }

    [Fact]
    public async Task TheProposalViewShowsTheProposalsHeadlinesAloneWithoutReadingAgain()
    {
        var (fake, context, page) = await OpenedAsync(Trial());
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        var reads = fake.OverviewRequests.Count;

        context.ShowProposalView(MarkupView.Proposal);

        Assert.Equal("130 of 142 words parse", page.TextCoverageMain);
        Assert.Equal("75 of 84 rebuilt exactly", page.AccuracyMain);
        Assert.Equal("142 words · 372,000 work steps in total", page.SpeedMain);
        Assert.Equal("18 warnings", page.WarningsCount);
        Assert.Equal(MotifAppStrings.OverviewProposalViewNote, page.ProposalNote);
        Assert.Equal(reads, fake.OverviewRequests.Count);
    }

    [Fact]
    public async Task SwitchingBackToThePairShowsBothSidesAgain()
    {
        var (_, context, page) = await OpenedAsync(Trial());
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        context.ShowProposalView(MarkupView.Today);

        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.Equal("118 → 130 of 142 words parse", page.TextCoverageMain);
        Assert.Equal(MotifAppStrings.OverviewProposalPairedNote, page.ProposalNote);
    }

    [Fact]
    public async Task ChoosingAViewRaisesTheFourHeadlinesAndTheNote()
    {
        var (_, context, page) = await OpenedAsync(Trial());
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        var raised = new List<string?>();
        page.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        context.ShowProposalView(MarkupView.Proposal);

        Assert.Contains(nameof(OverviewPageModel.TextCoverageMain), raised);
        Assert.Contains(nameof(OverviewPageModel.AccuracyMain), raised);
        Assert.Contains(nameof(OverviewPageModel.SpeedMain), raised);
        Assert.Contains(nameof(OverviewPageModel.WarningsCount), raised);
        Assert.Contains(nameof(OverviewPageModel.ProposalNote), raised);
    }

    [Fact]
    public async Task AHeadlineTheProposalLacksSaysSoInTheProposalViewInsteadOfShowingTodays()
    {
        var (_, context, page) = await OpenedAsync(Trial(), Proposal() with { Warnings = null });
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        context.ShowProposalView(MarkupView.Proposal);

        Assert.Equal(MotifAppStrings.OverviewProposalNone, page.WarningsCount);
        Assert.DoesNotContain("20", page.WarningsCount);
        Assert.Equal("130 of 142 words parse", page.TextCoverageMain);
        Assert.Equal(MotifAppStrings.OverviewProposalViewNote + " One headline has no value with this Proposal: " +
            "Grammar Warnings.", page.ProposalNote);
    }

    [Fact]
    public async Task AProposalWithNoNumbersShowsNoneInEveryHeadlineInTheProposalView()
    {
        var (_, context, page) = await OpenedAsync(null);
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        context.ShowProposalView(MarkupView.Proposal);

        foreach (var headline in new[] { page.TextCoverageMain, page.AccuracyMain, page.SpeedMain, page.WarningsCount })
            Assert.Equal(MotifAppStrings.OverviewProposalNone, headline);
        Assert.Equal(MotifAppStrings.TrialNotTriedSentence + " " + MotifAppStrings.OverviewProposalNoNumbersNote,
            page.ProposalNote);
    }

    [Fact]
    public async Task AProposalWithNoNumbersKeepsTodaysHeadlinesAndTheOldNoteInTheTodayView()
    {
        var (_, context, page) = await OpenedAsync(null);
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        context.ShowProposalView(MarkupView.Today);

        Assert.Equal("118 of 142 words parse", page.TextCoverageMain);
        Assert.Equal(MotifAppStrings.TrialNotTriedSentence + " " + MotifAppStrings.OverviewProposalTodayOnlyNote,
            page.ProposalNote);
    }

    [Fact]
    public async Task TheOldNumbersReasonLeadsTheProposalViewNote()
    {
        var stale = Trial() with { Freshness = TrialFreshness.BaselineMoved };
        var (_, context, page) = await OpenedAsync(stale);
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        context.ShowProposalView(MarkupView.Proposal);

        Assert.Equal(string.Format(MotifAppStrings.OverviewProposalViewOldNoteFormat,
            TrialStandingWords.BaselineMoved.OldNumbers), page.ProposalNote);
    }

    [Fact]
    public async Task TheViewSurvivesPagesAndARefreshAndResetsWhenTheProposalIsLeft()
    {
        var (_, context, _) = await OpenedAsync(Trial());
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        Assert.Equal(MarkupView.TodayToProposal, context.Lens!.View);

        context.ShowProposalView(MarkupView.Proposal);
        context.CurrentPage = WorkspacePage.Texts;
        context.CurrentPage = WorkspacePage.Overview;
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        Assert.Equal(MarkupView.Proposal, context.Lens!.View);

        await context.LeaveProposalAsync();
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        Assert.Equal(MarkupView.TodayToProposal, context.Lens!.View);

        context.ShowProposalView(MarkupView.Today);
        await context.OpenProposalAsync("proposal/two", CancellationToken.None);
        Assert.Equal(MarkupView.TodayToProposal, context.Lens!.View);
    }

    [Fact]
    public async Task OnlyTheOverviewIsInScopeAndOtherPagesAreToldTheViewIsToday()
    {
        var (_, context, _) = await OpenedAsync(Trial());
        Assert.Equal(MarkupView.Today, ProposalViewScope.ViewOf(context));
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        context.ShowProposalView(MarkupView.Proposal);

        Assert.Equal(MarkupView.Proposal, ProposalViewScope.ViewOf(context));
        Assert.Equal([WorkspacePage.Overview], ProposalViewScope.Pages);
    }

    internal static async Task<(FakeCommandClient Fake, WorkspaceContext Context, OverviewPageModel Page)> OpenedAsync(
        TrialRecord? trial, OverviewResponse? proposal = null, bool needsAssessment = false,
        OverviewResponse? today = null)
    {
        var fake = new FakeCommandClient();
        if (needsAssessment)
        {
            var now = DateTimeOffset.UtcNow;
            fake.ReadCurrentEvidenceCompletesWith(new CurrentEvidenceSnapshot("one", now, null, EvidenceFreshness.Current,
                new BaselineRecord("project-1", Token, "root", ProjectPath, now, now), null, null, null, null));
        }
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new OverviewPageModel(context);
        fake.OnOverview((request, _) => Task.FromResult(CommandOutcome<OverviewResponse>.Success(
            request.ProposalId is null ? today ?? Today() : proposal ?? Proposal())));
        fake.ReadTrialCompletesWith(trial);
        await context.OpenProjectAsync(ProjectPath);
        if (needsAssessment) context.Baseline = new WorkspaceBaseline(true, string.Empty, string.Empty, string.Empty, string.Empty, null);
        return (fake, context, page);
    }

    internal static OverviewResponse Today() => OverviewPageWordsTests.Populated();

    internal static OverviewResponse Proposal()
    {
        var today = OverviewPageWordsTests.Populated();
        return today with
        {
            TextCoverage = today.TextCoverage with { ParsedWords = 130 },
            Accuracy = today.Accuracy with { ApprovedWordsKept = 75 },
            Timing = today.Timing with { Work = new WordWorkSummary(142, 372_000, 60, 470) },
            Warnings = today.Warnings! with { Count = 22, WarningCount = 18 },
        };
    }

    internal static TrialRecord Trial() => new("job/trial", "proposal/one", "sha256:intent", Token, null,
        new Dictionary<string, string> { [AssessmentKinds.ParseTime] = "assessment/trial" }, ["dogs"], false, null,
        JobStatus.Completed, DateTimeOffset.UtcNow, TrialFreshness.Current)
    {
        Work = new TrialWork(142, 380_000, 372_000, -2.1, [], 0, 0),
    };
}
