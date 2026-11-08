using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins what the Timing and Warnings pages do when the open Proposal's read goes wrong or is late: a thrown or refused
/// read says so and is read again, cancellation is silent, no older read shows, and today's response is not kept.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalReadFailureTests
{
    private static readonly string Unreadable = string.Format(MotifAppStrings.ProposalPageUnreadableFormat,
        MotifAppStrings.TimingNoun);

    private static Task<CommandOutcome<TimingResponse>> Ok(TimingResponse response) =>
        Task.FromResult(CommandOutcome<TimingResponse>.Success(response));

    private static Task<CommandOutcome<TimingResponse>> Answer(TimingRequest request, TimingProposalViewTests.Side side) =>
        Ok(request.ProposalId is null
            ? TimingProposalViewTests.Respond(request, side.Today, ("morph_rule", 1))
            : TimingProposalViewTests.Respond(request, side.Proposals(request.ProposalId), ("morph_rule", 1)));

    private static Task<CommandOutcome<TimingResponse>> Thrown() =>
        Task.FromException<CommandOutcome<TimingResponse>>(new InvalidOperationException("the read broke"));

    private static Task<CommandOutcome<TimingResponse>> Refused() => Task.FromResult(
        CommandOutcome<TimingResponse>.Refused(new Refusal("read_failed", FailureReason.Refused, "no")));

    private static Task<CommandOutcome<TimingResponse>> Cancelled() =>
        Task.FromCanceled<CommandOutcome<TimingResponse>>(new CancellationToken(true));

    // Fails the first Proposal read the way the test says, then answers as the command does.
    private static TimingProposalViewTests.Side FailingOnce(Func<Task<CommandOutcome<TimingResponse>>> failure)
    {
        var side = new TimingProposalViewTests.Side();
        var failed = false;
        side.Hold = request =>
        {
            if (request.ProposalId is null || failed) return Answer(request, side);
            failed = true;
            return failure();
        };
        return side;
    }

    [Fact]
    public async Task TodaysOldResponseIsNotKeptByTheDigestAfterAFlipAndAWordSetChange()
    {
        var side = new TimingProposalViewTests.Side
        {
            Today = [new("a", 1_000), new("b", 2_000, true), new("c", 3_000)],
            Proposals = _ => [new("a", 900), new("b", 1_800, true), new("c", 2_700, true)],
        };
        WeakReference? parserWorkLimit = null;
        side.Hold = request =>
        {
            var answer = Answer(request, side);
            if (request.ProposalId is null && request.By == "kind" && request.WordSet == "parser-work-limit")
                parserWorkLimit ??= new WeakReference(answer.Result.Value);
            return answer;
        };
        var (_, context, page) = await TimingProposalViewTests.OpenedAsync(side);
        await page.SelectWordSetCommand.ExecuteAsync("parser-work-limit");
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.NotNull(page.HeadlineTotal);
        await context.LeaveProposalAsync(CancellationToken.None);

        page.SlowestCount = 2;
        await page.SelectWordSetCommand.ExecuteAsync("slowest");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.NotNull(parserWorkLimit);
        Assert.False(parserWorkLimit!.IsAlive);
        GC.KeepAlive(page);
    }

    [Fact]
    public async Task ALateReadForTheOldProposalIsNeverShownWhileTheNewOnesReadIsStillOut()
    {
        var side = new TimingProposalViewTests.Side { Proposals = id => id == "proposal/two"
            ? [new("a", 100), new("b", 200), new("c", 300), new("d", 400)] : TimingProposalViewTests.ProposalCalm };
        var (_, context, page) = await TimingProposalViewTests.OpenedAsync(side, onTimingPage: false);
        var gateA = new TaskCompletionSource<CommandOutcome<TimingResponse>>();
        var gateB = new TaskCompletionSource<CommandOutcome<TimingResponse>>();
        side.Hold = request => request.ProposalId switch
        {
            "proposal/one" => gateA.Task,
            "proposal/two" => gateB.Task,
            _ => Answer(request, side),
        };
        context.OpenPage(WorkspacePage.Timing);
        await Task.Delay(50);

        var opening = context.OpenProposalAsync("proposal/two", CancellationToken.None);
        await Task.Delay(50);
        context.ShowProposalView(MarkupView.Proposal);
        gateA.SetResult(await Ok(TimingProposalViewTests.Respond(new TimingRequest(TimingProposalViewTests.ProjectPath, ProposalId: "proposal/one"),
            TimingProposalViewTests.ProposalCalm, ("morph_rule", 1))));
        await Task.Delay(50);

        Assert.Equal(MotifAppStrings.OverviewProposalReading, page.HeadlineTotal);
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.DoesNotContain("10%", page.HeadlineTotal);
        gateB.SetResult(await Ok(TimingProposalViewTests.Respond(new TimingRequest(TimingProposalViewTests.ProjectPath, ProposalId: "proposal/two"),
            side.Proposals("proposal/two"), ("morph_rule", 1))));
        await opening;
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal("About 90% less parser work than the Baseline", page.HeadlineTotal);
    }

    [Fact]
    public async Task AThrownReadSaysItCouldNotBeReadAndIsReadAgainWhenThePageIsNextShown()
    {
        var side = FailingOnce(Thrown);
        var (fake, context, page) = await TimingProposalViewTests.OpenedAsync(side);

        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal(MotifAppStrings.ProposalWorkNotComparable, page.HeadlineTotal);
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.Contains(Unreadable, page.ProposalNote);
        Assert.Equal(1, TimingProposalViewTests.ProposalReads(fake));

        context.OpenPage(WorkspacePage.Overview);
        context.OpenPage(WorkspacePage.Timing);
        await Task.Delay(50);

        Assert.Equal(2, TimingProposalViewTests.ProposalReads(fake));
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal("About 10% less parser work than the Baseline", page.HeadlineTotal);
    }

    [Fact]
    public async Task ARefusedReadIsNotHeldAsTheAnswerAndIsReadAgainOnTheNextPublish()
    {
        var side = FailingOnce(Refused);
        var (fake, context, page) = await TimingProposalViewTests.OpenedAsync(side);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal(MotifAppStrings.ProposalWorkNotComparable, page.HeadlineTotal);

        context.PublishEvidence(new WorkspaceEvidence(TimingProposalViewTests.Assessment(), DateTimeOffset.Now, WasRerun: false));
        await context.EvidencePublication;

        Assert.Equal(2, TimingProposalViewTests.ProposalReads(fake));
        Assert.Equal("About 10% less parser work than the Baseline", page.HeadlineTotal);
    }

    [Fact]
    public async Task LeavingAndReopeningTheSameProposalReadsAgainAfterAFailedRead()
    {
        var side = FailingOnce(Thrown);
        var (fake, context, page) = await TimingProposalViewTests.OpenedAsync(side);
        await context.LeaveProposalAsync(CancellationToken.None);

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        context.ShowProposalView(MarkupView.Proposal);

        Assert.Equal(2, TimingProposalViewTests.ProposalReads(fake));
        Assert.Equal("About 10% less parser work than the Baseline", page.HeadlineTotal);
    }

    [Fact]
    public async Task ACancelledReadIsSilentAndSaysNothingWentWrong()
    {
        var side = FailingOnce(Cancelled);
        var (_, context, page) = await TimingProposalViewTests.OpenedAsync(side);

        context.ShowProposalView(MarkupView.Proposal);

        Assert.Equal(MotifAppStrings.OverviewProposalReading, page.HeadlineTotal);
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.DoesNotContain(Unreadable, page.ProposalNote ?? string.Empty);
    }

    [Fact]
    public async Task AFocusedPageWhoseCommandCannotRunStillReadsTheProposalForTheNewWords()
    {
        var side = new TimingProposalViewTests.Side();
        var todayGate = new TaskCompletionSource<CommandOutcome<TimingResponse>>();
        side.Hold = request => request.ProposalId is null && request.ExplicitWords is { Count: 2 }
            ? todayGate.Task : Answer(request, side);
        var fake = new FakeCommandClient();
        fake.OnTiming((request, _) => side.Hold(request));
        fake.OnReadTrial((request, _) => Task.FromResult(
            CommandOutcome<TrialRecord?>.Success(TimingProposalViewTests.TrialFor(request.ProposalId, true))));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new TimingPageModel(context);
        await context.OpenProjectAsync(TimingProposalViewTests.ProjectPath);
        context.OpenPage(WorkspacePage.Timing);
        await context.PublishCurrentEvidenceAsync(TimingProposalViewTests.StoredSnapshot());
        context.Open(new OpenTimingRequest(["a", "b"], null));
        await Task.Delay(50);
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        context.ShowProposalView(MarkupView.Proposal);

        context.Open(new OpenTimingRequest(["c"], null));
        await Task.Delay(100);

        Assert.Equal(MotifAppStrings.ProposalWorkNotComparable, page.HeadlineTotal);
        todayGate.SetResult(CommandOutcome<TimingResponse>.Refused(new Refusal("none", FailureReason.Refused, "none")));
    }

    private static string WarningsUnreadable => string.Format(MotifAppStrings.ProposalPageUnreadableFormat,
        MotifAppStrings.WarningsFindingsNoun);

    private static Task<CommandOutcome<StoredGrammarCheckResponse>> FailOnce(GrammarCheckRequest request, ref int left,
        Func<Task<CommandOutcome<StoredGrammarCheckResponse>>> failure) =>
        request.ProposalId is not null && left-- > 0 ? failure() : WarningsProposalKeyTests.Answer(WarningsProposalKeyTests.Of(request.ProposalId));

    [Fact]
    public async Task AThrownCheckSaysItCouldNotBeReadAndIsReadAgainWhenTheWarningsPageIsNextShown()
    {
        var left = 1;
        var (fake, context, page) = WarningsProposalKeyTests.Build(request => FailOnce(request, ref left, () =>
            Task.FromException<CommandOutcome<StoredGrammarCheckResponse>>(new InvalidOperationException("broke"))));
        await context.OpenProjectAsync(WarningsProposalKeyTests.ProjectPath);
        context.OpenPage(WorkspacePage.Warnings);
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        context.ShowProposalView(MarkupView.Proposal);
        Assert.Contains(WarningsUnreadable, page.ProposalNote);
        Assert.Equal(MotifAppStrings.ProposalPageNone, page.ProposalNoneText);
        Assert.Equal(1, fake.StoredGrammarCheckRequests.Count(request => request.ProposalId is not null));

        context.OpenPage(WorkspacePage.Overview);
        context.OpenPage(WorkspacePage.Warnings);
        await Task.Delay(50);

        Assert.Equal(2, fake.StoredGrammarCheckRequests.Count(request => request.ProposalId is not null));
        Assert.Equal(["b", "d", "e"], WarningsProposalKeyTests.Codes(page));
    }

    [Fact]
    public async Task LeavingAndReopeningTheSameProposalReadsItsCheckAgainAfterARefusedRead()
    {
        var left = 1;
        var (fake, context, page) = WarningsProposalKeyTests.Build(request => FailOnce(request, ref left, () =>
            Task.FromResult(CommandOutcome<StoredGrammarCheckResponse>.Refused(new Refusal("no", FailureReason.Refused, "no")))));
        await context.OpenProjectAsync(WarningsProposalKeyTests.ProjectPath);
        context.OpenPage(WorkspacePage.Warnings);
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        await context.LeaveProposalAsync(CancellationToken.None);

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        context.ShowProposalView(MarkupView.Proposal);

        Assert.Equal(2, fake.StoredGrammarCheckRequests.Count(request => request.ProposalId is not null));
        Assert.Equal(["b", "d", "e"], WarningsProposalKeyTests.Codes(page));
    }

    [Fact]
    public async Task ACancelledCheckReadIsSilent()
    {
        var left = 1;
        var (_, context, page) = WarningsProposalKeyTests.Build(request => FailOnce(request, ref left, () =>
            Task.FromCanceled<CommandOutcome<StoredGrammarCheckResponse>>(new CancellationToken(true))));
        await context.OpenProjectAsync(WarningsProposalKeyTests.ProjectPath);
        context.OpenPage(WorkspacePage.Warnings);
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        context.ShowProposalView(MarkupView.Proposal);

        Assert.Equal(MotifAppStrings.OverviewProposalReading, page.ProposalNoneText);
        Assert.DoesNotContain(WarningsUnreadable, page.ProposalNote ?? string.Empty);
    }

    [Fact]
    public async Task TheWarningsPairViewShowsAReadingNoteWhileTheProposalsCheckIsRead()
    {
        var gate = new TaskCompletionSource<CommandOutcome<StoredGrammarCheckResponse>>();
        var (_, context, page) = WarningsProposalKeyTests.Build(request =>
            request.ProposalId is null ? WarningsProposalKeyTests.Answer(WarningsProposalKeyTests.Of(null)) : gate.Task);
        await context.OpenProjectAsync(WarningsProposalKeyTests.ProjectPath);
        context.OpenPage(WorkspacePage.Warnings);
        var opening = context.OpenProposalAsync("proposal/one", CancellationToken.None);
        await Task.Delay(50);

        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.Equal(MotifAppStrings.WarningsProposalReadingNote, page.ProposalNote);
        Assert.True(page.HasProposalNote);

        gate.SetResult(CommandOutcome<StoredGrammarCheckResponse>.Success(
            new StoredGrammarCheckResponse(WarningsProposalKeyTests.Of("proposal/one"))));
        await opening;
        Assert.NotEqual(MotifAppStrings.WarningsProposalReadingNote, page.ProposalNote);
        Assert.Equal(MotifAppStrings.WarningsProposalPairedNote, page.ProposalNote);
    }
}
