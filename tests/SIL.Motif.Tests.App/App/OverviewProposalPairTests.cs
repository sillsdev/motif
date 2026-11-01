using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that the Overview, in Proposal mode, reads its query once for today and once for the open Proposal's Trial
/// and shows each headline as the pair, and says plainly when there is no second number to show.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class OverviewProposalPairTests
{
    private static readonly string ProjectPath = Path.Combine(Path.GetTempPath(), "sample.fwdata");

    private static readonly BaselineToken Token = new(
        "project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));

    [Fact]
    public async Task OpeningAProposalWithATrialShowsTodaysHeadlinesBesideTheProposals()
    {
        var (fake, context, page) = await OpenedAsync(Trial());

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        Assert.Equal("118 → 130 of 142 words parse", page.TextCoverageMain);
        Assert.Equal("71 → 75 of 84 rebuilt exactly", page.AccuracyMain);
        Assert.Equal("142 words · 380,000 work steps in total → 142 words · 372,000 work steps in total", page.SpeedMain);
        Assert.Equal("20 warnings → 18 warnings", page.WarningsCount);
        Assert.True(page.HasProposalNote);
        Assert.Equal(MotifAppStrings.OverviewProposalPairedNote, page.ProposalNote);
        Assert.Equal(1, fake.OverviewRequests.Count(request => request.ProposalId == "proposal/one"));
    }

    [Fact]
    public async Task EachSetIsReadOnceNotOncePerHeadline()
    {
        var (fake, context, page) = await OpenedAsync(Trial());
        var before = fake.OverviewRequests.Count;

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        _ = (page.TextCoverageMain, page.AccuracyMain, page.SpeedMain, page.WarningsCount);

        var reads = fake.OverviewRequests.Skip(before).ToArray();
        Assert.Equal(1, reads.Count(request => request.ProposalId is null));
        Assert.Equal(1, reads.Count(request => request.ProposalId == "proposal/one"));
    }

    [Fact]
    public async Task HeadlinesWithDifferentSelectionsKeepEachSideWhole()
    {
        var (_, context, page) = await OpenedAsync(Trial(), Proposal() with { SelectionWordCount = 150 });

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        Assert.Equal("118 of 142 words parse → 130 of 150 words parse", page.TextCoverageMain);
    }

    [Fact]
    public async Task LeavingTheProposalReturnsTheHeadlinesToToday()
    {
        var (_, context, page) = await OpenedAsync(Trial());
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        await context.LeaveProposalAsync();

        Assert.Equal("118 of 142 words parse", page.TextCoverageMain);
        Assert.Equal("71 of 84 rebuilt exactly", page.AccuracyMain);
        Assert.Equal("20 warnings", page.WarningsCount);
        Assert.False(page.HasProposalNote);
    }

    [Fact]
    public async Task AProposalWithNoTrialShowsTodaysNumbersAndSaysNothingWasTried()
    {
        var (fake, context, page) = await OpenedAsync(null);
        var before = fake.OverviewRequests.Count;

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        Assert.Equal("118 of 142 words parse", page.TextCoverageMain);
        Assert.DoesNotContain(fake.OverviewRequests.Skip(before), request => request.ProposalId is not null);
        Assert.Equal(
            MotifAppStrings.TrialNotTriedSentence + " " + MotifAppStrings.OverviewProposalTodayOnlyNote,
            page.ProposalNote);
    }

    [Fact]
    public async Task ATrialThatIsStillRunningShowsTodaysNumbersAndSaysTrying()
    {
        var running = Trial() with
        {
            AssessmentIds = new Dictionary<string, string>(),
            Status = JobStatus.Running,
            CompletedUtc = null,
            Freshness = TrialFreshness.Incomplete,
        };
        var (_, context, page) = await OpenedAsync(running);

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        Assert.Equal("118 of 142 words parse", page.TextCoverageMain);
        Assert.Equal(MotifAppStrings.TrialTrying + " " + MotifAppStrings.OverviewProposalTodayOnlyNote,
            page.ProposalNote);
    }

    [Fact]
    public async Task ATrialThatDidNotFinishShowsTodaysNumbersAndSaysSo()
    {
        var failed = Trial() with
        {
            AssessmentIds = new Dictionary<string, string>(),
            Status = JobStatus.Failed,
            Freshness = TrialFreshness.Incomplete,
        };
        var (_, context, page) = await OpenedAsync(failed);

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        Assert.Equal("118 of 142 words parse", page.TextCoverageMain);
        Assert.Equal(MotifAppStrings.TrialDidNotFinishSentence + " " + MotifAppStrings.OverviewProposalTodayOnlyNote,
            page.ProposalNote);
    }

    [Fact]
    public async Task AStaleTrialStillPairsAndCallsItsNumbersOld()
    {
        var stale = Trial() with { Freshness = TrialFreshness.BaselineMoved };
        var (_, context, page) = await OpenedAsync(stale);

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        Assert.Equal("118 → 130 of 142 words parse", page.TextCoverageMain);
        Assert.Equal(string.Format(MotifAppStrings.OverviewProposalOldPairedNoteFormat,
            MotifAppStrings.TrialBaselineMovedOldNumbers), page.ProposalNote);
    }

    [Fact]
    public async Task AProposalWhoseNumbersCannotBeReadShowsTodaysAndSaysItCouldNotRead()
    {
        var (fake, context, page) = await OpenedAsync(Trial());
        fake.OnOverview((request, _) => Task.FromResult(request.ProposalId is null
            ? CommandOutcome<OverviewResponse>.Success(Today())
            : CommandOutcome<OverviewResponse>.Refused(new Refusal("trial.none", FailureReason.NotFound,
                "No Trial is recorded for this Proposal."))));

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        Assert.Equal("118 of 142 words parse", page.TextCoverageMain);
        Assert.Equal(MotifAppStrings.OverviewProposalUnreadableNote + " " + MotifAppStrings.OverviewProposalTodayOnlyNote,
            page.ProposalNote);
    }

    [Fact]
    public async Task TheNoteNamesTheFourHeadlinesAndSaysTheRestIsToday()
    {
        var (_, context, page) = await OpenedAsync(Trial());

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        Assert.Contains("four headline numbers", page.ProposalNote);
        Assert.Contains("Everything else on this page is today's", page.ProposalNote);
        Assert.DoesNotContain("Each number", page.ProposalNote);
    }

    private const string FourHeadlines = "The four headline numbers read today's value, then the value with this Proposal. " +
        "Everything else on this page is today's.";

    [Fact]
    public async Task OneHeadlineTheProposalHasNoValueForShowsTodaysAloneAndTheNoteSaysExactlyWhich()
    {
        var (_, context, page) = await OpenedAsync(Trial(), Proposal() with { Warnings = null });

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        Assert.Equal("20 warnings", page.WarningsCount);
        Assert.Equal("118 → 130 of 142 words parse", page.TextCoverageMain);
        Assert.Equal(FourHeadlines + " One headline shows today's value only: Grammar Warnings. " +
            "The Proposal has no value to set beside today's.", page.ProposalNote);
    }

    [Fact]
    public async Task SpeedDoesNotPairWhenTheTwoTriesCannotBeComparedAsOnTheProposalsPage()
    {
        var incomparable = Trial() with { Work = new TrialWork(142, null, null, null, [], 0, 0) };
        var (_, context, page) = await OpenedAsync(incomparable);

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        Assert.Equal("142 words · 380,000 work steps in total", page.SpeedMain);
        Assert.Equal("118 → 130 of 142 words parse", page.TextCoverageMain);
        Assert.Equal(FourHeadlines + " One headline shows today's value only: Speed. " +
            "The work steps of the two tries cannot be compared.", page.ProposalNote);
    }

    [Fact]
    public async Task WhenTodayHasNoParseTheNoteBlamesTodayNotTheProposal()
    {
        var (fake, context, page) = await OpenedAsync(Trial());
        fake.OnOverview((request, _) => Task.FromResult(CommandOutcome<OverviewResponse>.Success(
            request.ProposalId is null ? Today() with { AssessmentId = null } : Proposal())));
        await context.PublishBaselineCapturedAsync(openFirstRunSetup: false);
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        Assert.DoesNotContain("→", page.TextCoverageMain);
        Assert.DoesNotContain("→", page.AccuracyMain);
        Assert.DoesNotContain("→", page.SpeedMain);
        Assert.Equal(FourHeadlines + " 3 headlines show today's value only: Text Coverage, Approved Analyses Kept, " +
            "Speed. Today has no result to set the Proposal's beside.", page.ProposalNote);
    }

    [Fact]
    public async Task ARunThatFinishesAfterLeavingDoesNotBringTheProposalBack()
    {
        var (fake, context, page) = await OpenedAsync(Trial());
        var pending = new TaskCompletionSource<CommandOutcome<OverviewResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.OnOverview((request, _) => request.ProposalId is null
            ? Task.FromResult(CommandOutcome<OverviewResponse>.Success(Today())) : pending.Task);

        var opening = context.OpenProposalAsync("proposal/one", CancellationToken.None);
        var leaving = context.LeaveProposalAsync();
        pending.SetResult(CommandOutcome<OverviewResponse>.Success(Proposal()));
        await Task.WhenAll(opening, leaving);
        await context.EvidencePublication;

        Assert.Equal("118 of 142 words parse", page.TextCoverageMain);
        Assert.False(page.HasProposalNote);
    }

    private static async Task<(FakeCommandClient Fake, WorkspaceContext Context, OverviewPageModel Page)> OpenedAsync(
        TrialRecord? trial, OverviewResponse? proposal = null)
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new OverviewPageModel(context);
        fake.OnOverview((request, _) => Task.FromResult(CommandOutcome<OverviewResponse>.Success(
            request.ProposalId is null ? Today() : proposal ?? Proposal())));
        fake.ReadTrialCompletesWith(trial);
        await context.OpenProjectAsync(ProjectPath);
        return (fake, context, page);
    }

    private static OverviewResponse Today() => OverviewPageWordsTests.Populated();

    private static OverviewResponse Proposal()
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

    private static TrialRecord Trial() => new("job/trial", "proposal/one", "sha256:intent", Token, null,
        new Dictionary<string, string> { [AssessmentKinds.ParseTime] = "assessment/trial" }, ["dogs"], false, null,
        JobStatus.Completed, DateTimeOffset.UtcNow, TrialFreshness.Current)
    {
        Work = new TrialWork(142, 380_000, 372_000, -2.1, [], 0, 0),
    };
}
