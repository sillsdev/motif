using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProposalDocumentApplyTests
{
    [UiThreadFact]
    public async Task ApplyingThePendingDraftKeepsItsPublishedReceiptAfterItsCardCloses()
    {
        var fake = new FakeCommandClient();
        var change = new PendingChange("one", "wordform/one", "one", "approve", "assessment/one", "reading", ["op"]);
        fake.PendingChangesIs(new("draft/one", "revision/one", [change], [new ChangeFit("one", true, [])]));
        fake.MeasurePendingHandler = (request, _) => Task.FromResult(CommandOutcome<MeasurePendingResult>.Success(
            SIL.Motif.Tests.Support.TestFixtures.PendingMeasureResponseFactory.Create("job/pending", request.Revision!,
                FakeCommandClient.CompleteNumbers)));
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var documents = new OpenProposalDocuments(context);
        var pending = documents.Open("draft/one");
        pending.Card = new ProposalCardViewModel(new("draft/one", "Draft", "proposed", ProposalGroup.NotAppliedYet,
            true, [], TrialStanding.NotTried, null, []) { IsDraft = true });
        await pending.RefreshAsync();
        await pending.MeasurePendingCommand.ExecuteAsync(null);
        await pending.PrepareApplyCommand.ExecuteAsync(null);
        Assert.True(pending.IsConfirmingApply);
        var receipt = new ApplyProjection("published/one", false, "Saved", [], "effect",
            new AppliedLogEntrySummary("published/one", "now", "Person", "intent/one"));
        fake.ApplyPendingHandler = (_, _) =>
        {
            pending.Card = new ProposalCardViewModel(new("draft/one", "Draft", "applied", ProposalGroup.SetAside,
                false, [], TrialStanding.Current, null, []));
            fake.PendingChangesIs(new(null, "none", [], []));
            return Task.FromResult(CommandOutcome<ApplyPendingResult>.Success(ApplyPendingResult.AppliedWith(receipt, "Saved")));
        };
        await pending.ConfirmApplyCommand.ExecuteAsync(null);
        Assert.Single(fake.ApplyPendingRequests);
        Assert.Same(receipt, pending.Receipt);
        Assert.Empty(fake.ApplyProposalRequests);
        await documents.StopAsync();
        await context.StopProjectWorkAsync();
    }

    [UiThreadFact]
    public async Task CheckingThePendingDraftLeavesAnotherCandidatesLensBeforeMeasuring()
    {
        var fake = new FakeCommandClient();
        var change = new PendingChange("one", "wordform/one", "one", "approve", "assessment/one", "reading", ["op"]);
        fake.PendingChangesIs(new("draft/one", "revision/one", [change], [new ChangeFit("one", true, [])]));
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        await context.OpenProposalAsync("another", CancellationToken.None);
        var documents = new OpenProposalDocuments(context);
        var pending = documents.Open("draft/one");
        pending.Card = new ProposalCardViewModel(new("draft/one", "Draft", "proposed", ProposalGroup.NotAppliedYet,
            true, [], TrialStanding.NotTried, null, []) { IsDraft = true });
        await pending.RefreshAsync();
        Assert.Empty(fake.MeasurePendingRequests);
        fake.MeasurePendingHandler = (request, _) =>
        {
            Assert.Null(context.Lens);
            Assert.Null(context.Changes.TargetProposalId);
            return Task.FromResult(CommandOutcome<MeasurePendingResult>.Success(
                SIL.Motif.Tests.Support.TestFixtures.PendingMeasureResponseFactory.Create("job/pending", request.Revision!,
                    FakeCommandClient.CompleteNumbers)));
        };
        await pending.MeasurePendingCommand.ExecuteAsync(null);
        Assert.Equal("draft/one", Assert.Single(fake.MeasurePendingRequests).DraftId);
        Assert.Empty(fake.ApplyProposalRequests);
        await documents.StopAsync();
        await context.StopProjectWorkAsync();
    }

    [UiThreadFact]
    public async Task ApplyRequiresHumanConfirmationTargetsOneCandidateAndInvalidatesTheOthers()
    {
        var fake = new FakeCommandClient();
        var trial = new TrialRecord("job/one", "one", "intent/one", WorkspaceContextTests.Assessment().Baseline.Token,
            null, new Dictionary<string, string>(), [], false, null, JobStatus.Completed, DateTimeOffset.UtcNow,
            TrialFreshness.Current);
        fake.ReadTrialCompletesWith(trial);
        ProposalStatusFakes.GiveItASavedDifference(fake);
        fake.PendingChangesIs(new PendingChangesSnapshot("one", "revision/one", [], []));
        fake.ShowProposalHandler = (request, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(
            new(request.ProposalId, "proposed", request.ProposalId, null, "intent/one", [])));
        fake.Review = new ProposalReviewResponse("one", "intent/one", "revision/one",
            [new GrammarChangeEntry("entry", 0, "A change", ["op/one"], [], "content", false,
                "composer", "A change", [], [], [])], trial, new(true, false, false, null, [], false), [], 0);
        var receipt = new ApplyProjection("one", false, "Saved", [], "effect",
            new AppliedLogEntrySummary("one", "now", "Person", "intent/one"));
        fake.ApplyProposalHandler = (_, _) => Task.FromResult(CommandOutcome<ApplyPendingResult>.Success(
            ApplyPendingResult.AppliedWith(receipt, "Saved")));
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var documents = new OpenProposalDocuments(context);
        var one = documents.Open("one");
        var two = documents.Open("two");
        await documents.RefreshAsync();
        await one.ConfirmApplyCommand.ExecuteAsync(null);
        Assert.Empty(fake.ApplyProposalRequests);
        await one.PrepareApplyCommand.ExecuteAsync(null);
        Assert.Empty(fake.ApplyProposalRequests);
        Assert.True(one.IsConfirmingApply);
        Assert.True(one.CanApply);
        await one.ConfirmApplyCommand.ExecuteAsync(null);
        Assert.Equal("one", Assert.Single(fake.ApplyProposalRequests).ProposalId);
        Assert.Same(receipt, one.Receipt);
        Assert.True(two.IsOutdated);
        Assert.False(two.CanApply);
        Assert.Empty(fake.MeasurePendingRequests);
        await documents.StopAsync();
        await context.StopProjectWorkAsync();
    }
}
