using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins three corners of the Proposal pages' reads: the page's own cancellation is not a failed read, the Warnings
/// reading note does not claim today was checked when it was not, and an older read never lands under a newer one.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalReadLeftoverTests
{
    private static readonly string TimingUnreadable = string.Format(MotifAppStrings.ProposalPageUnreadableFormat,
        MotifAppStrings.TimingNoun);

    private static readonly string WarningsUnreadable = string.Format(MotifAppStrings.ProposalPageUnreadableFormat,
        MotifAppStrings.WarningsFindingsNoun);

    private static Task<CommandOutcome<TimingResponse>> Answer(TimingRequest request, TimingProposalViewTests.Side side) =>
        Task.FromResult(CommandOutcome<TimingResponse>.Success(request.ProposalId is null
            ? TimingProposalViewTests.Respond(request, side.Today, ("morph_rule", 1))
            : TimingProposalViewTests.Respond(request, side.Proposals(request.ProposalId), ("morph_rule", 1))));

    private static CommandOutcome<TimingResponse> Total(IReadOnlyList<TimingProposalViewTests.Word> words, string? proposalId = null) =>
        CommandOutcome<TimingResponse>.Success(TimingProposalViewTests.Respond(
            new TimingRequest(TimingProposalViewTests.ProjectPath, ProposalId: proposalId), words, ("morph_rule", 1)));

    [Fact]
    public async Task TheTimingPagesOwnCancellationIsNotAFailedRead()
    {
        var side = new TimingProposalViewTests.Side();
        var (_, context, page) = await TimingProposalViewTests.OpenedAsync(side);
        var gate = new TaskCompletionSource<CommandOutcome<TimingResponse>>();
        side.Hold = request => request.ProposalId == "proposal/two" ? gate.Task : Answer(request, side);
        using var cancel = new CancellationTokenSource();
        var opening = context.OpenProposalAsync("proposal/two", cancel.Token);
        await Task.Delay(50);

        await cancel.CancelAsync();
        gate.TrySetCanceled(cancel.Token);
        try { await opening; }
        catch (OperationCanceledException) { }
        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.DoesNotContain(TimingUnreadable, page.ProposalNote ?? string.Empty);
    }

    [Fact]
    public async Task TheWarningsPagesOwnCancellationIsNotAFailedRead()
    {
        var gate = new TaskCompletionSource<CommandOutcome<StoredGrammarCheckResponse>>();
        var (_, context, page) = WarningsProposalKeyTests.Build(request => request.ProposalId == "proposal/two"
            ? gate.Task : WarningsProposalKeyTests.Answer(WarningsProposalKeyTests.Of(request.ProposalId)));
        await context.OpenProjectAsync(WarningsProposalKeyTests.ProjectPath);
        context.OpenPage(WorkspacePage.Warnings);
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        using var cancel = new CancellationTokenSource();
        var opening = context.OpenProposalAsync("proposal/two", cancel.Token);
        await Task.Delay(50);

        await cancel.CancelAsync();
        gate.TrySetCanceled(cancel.Token);
        try { await opening; }
        catch (OperationCanceledException) { }
        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.DoesNotContain(WarningsUnreadable, page.ProposalNote ?? string.Empty);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheWarningsReadingNoteDoesNotSayTodayWasCheckedWhenItWasRefusedOrNotRun(bool refused)
    {
        var gate = new TaskCompletionSource<CommandOutcome<StoredGrammarCheckResponse>>();
        var (_, context, page) = WarningsProposalKeyTests.Build(request => request.ProposalId is not null ? gate.Task
            : Task.FromResult(refused
                ? CommandOutcome<StoredGrammarCheckResponse>.Refused(new Refusal("read_failed", FailureReason.Refused, "no"))
                : CommandOutcome<StoredGrammarCheckResponse>.Success(new StoredGrammarCheckResponse(null))));
        await context.OpenProjectAsync(WarningsProposalKeyTests.ProjectPath);
        context.OpenPage(WorkspacePage.Warnings);
        var opening = context.OpenProposalAsync("proposal/one", CancellationToken.None);
        await Task.Delay(50);

        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.Equal(MotifAppStrings.WarningsProposalReadingTodayNotCheckedNote, page.ProposalNote);
        gate.SetResult(CommandOutcome<StoredGrammarCheckResponse>.Success(
            new StoredGrammarCheckResponse(WarningsProposalKeyTests.Of("proposal/one"))));
        await opening;
    }

    [Fact]
    public async Task NoOlderReadLandsUnderTheNewProposalWhileItsTodayReloadIsAlsoStillOut()
    {
        var side = new TimingProposalViewTests.Side { Proposals = id => id == "proposal/two"
            ? [new("a", 100), new("b", 200), new("c", 300), new("d", 400)] : TimingProposalViewTests.ProposalCalm };
        var (_, context, page) = await TimingProposalViewTests.OpenedAsync(side, onTimingPage: false);
        var gateA = new TaskCompletionSource<CommandOutcome<TimingResponse>>();
        var gateB = new TaskCompletionSource<CommandOutcome<TimingResponse>>();
        var gateToday = new TaskCompletionSource<CommandOutcome<TimingResponse>>();
        var todayHeld = false;
        side.Hold = request => request.ProposalId switch
        {
            "proposal/one" => gateA.Task,
            "proposal/two" => gateB.Task,
            null when todayHeld => gateToday.Task,
            _ => Answer(request, side),
        };
        context.OpenPage(WorkspacePage.Timing);
        await Task.Delay(50);
        var opening = context.OpenProposalAsync("proposal/two", CancellationToken.None);
        await Task.Delay(50);
        todayHeld = true;
        context.PublishEvidence(new WorkspaceEvidence(TimingProposalViewTests.Assessment(), DateTimeOffset.Now, WasRerun: false));
        await Task.Delay(50);
        context.ShowProposalView(MarkupView.Proposal);

        gateA.SetResult(Total(TimingProposalViewTests.ProposalCalm, "proposal/one"));
        await Task.Delay(50);
        Assert.Equal(MotifAppStrings.OverviewProposalReading, page.HeadlineTotal);
        gateToday.SetResult(Total(side.Proposals("proposal/two").Select(word => word with { Steps = word.Steps * 10 }).ToArray()));
        await Task.Delay(50);
        Assert.Equal(MotifAppStrings.OverviewProposalReading, page.HeadlineTotal);

        gateB.SetResult(Total(side.Proposals("proposal/two"), "proposal/two"));
        await opening;
        Assert.Equal("About 90% less parser work than the Baseline", page.HeadlineTotal);
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.DoesNotContain("9,000", page.HeadlineTotal);
    }
}
