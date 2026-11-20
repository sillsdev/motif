using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// The Apply steps of a pending Proposal document that lost their tests with the old Review screens: the question
/// is cancellable and writes nothing, a refusal shows without a receipt or a refresh, a successful Apply refreshes
/// the project once, and a document that is read again or outdated stops asking.
/// </summary>
public sealed class ProposalApplyStepsTests
{
    private sealed class Opened(FakeCommandClient fake, WorkspaceContext context, ProposalDocumentViewModel document,
        OpenProposalDocuments documents) : IAsyncDisposable
    {
        public FakeCommandClient Fake { get; } = fake;
        public WorkspaceContext Context { get; } = context;
        public ProposalDocumentViewModel Document { get; } = document;
        public int Refreshes { get; set; }

        public async ValueTask DisposeAsync()
        {
            await documents.StopAsync();
            await Context.StopProjectWorkAsync();
        }
    }

    private static async Task<Opened> OpenMeasuredPendingAsync()
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
        var opened = new Opened(fake, context, pending, documents);
        context.RefreshProjectCommand = new AsyncRelayCommand(() => { opened.Refreshes++; return Task.CompletedTask; });
        return opened;
    }

    private static readonly ApplyProjection Receipt = new("published/one", false, "Saved", [], "effect",
        new AppliedLogEntrySummary("published/one", "now", "Person", "intent/one"));

    [Fact]
    public async Task TheApplyButtonStepsAsideWhileItsConfirmationIsOpenSoOnlyOneApplyShows()
    {
        await using var opened = await OpenMeasuredPendingAsync();
        Assert.True(opened.Document.ShowsPrepareApply);

        await opened.Document.PrepareApplyCommand.ExecuteAsync(null);

        Assert.True(opened.Document.IsConfirmingApply);
        Assert.False(opened.Document.ShowsPrepareApply);
        opened.Document.CancelApplyCommand.Execute(null);
        Assert.True(opened.Document.ShowsPrepareApply);
    }

    [Fact]
    [Fact]
    public async Task ApplyReappearsWhenEvidenceChangesUnderAnOpenConfirmation()
    {
        await using var opened = await OpenMeasuredPendingAsync();
        await opened.Document.PrepareApplyCommand.ExecuteAsync(null);
        Assert.False(opened.Document.ShowsPrepareApply);

        opened.Document.InvalidateEvidence();

        Assert.False(opened.Document.IsConfirmingApply);
        Assert.True(opened.Document.ShowsPrepareApply);
    }

    [Fact]
    public async Task CancellingThePreparedApplyClosesTheQuestionAndWritesNothing()
    {
        await using var opened = await OpenMeasuredPendingAsync();
        await opened.Document.PrepareApplyCommand.ExecuteAsync(null);
        Assert.True(opened.Document.IsConfirmingApply);

        opened.Document.CancelApplyCommand.Execute(null);
        await opened.Document.ConfirmApplyCommand.ExecuteAsync(null);

        Assert.False(opened.Document.IsConfirmingApply);
        Assert.Empty(opened.Fake.ApplyPendingRequests);
        Assert.Null(opened.Document.Receipt);
        Assert.Equal(0, opened.Refreshes);
    }

    [Fact]
    public async Task APressWhileTheDocumentIsBeingReadWaitsForTheReadAndThenAsksForConfirmation()
    {
        await using var opened = await OpenMeasuredPendingAsync();
        var shown = await opened.Fake.ShowProposalAsync(new("project.fwdata", "1.0", "draft/one"), CancellationToken.None);
        var gate = new TaskCompletionSource();
        opened.Fake.ShowProposalHandler = async (_, _) =>
        {
            await gate.Task;
            return shown;
        };
        var reading = opened.Document.RefreshAsync();
        Assert.True(opened.Document.IsLoading);
        Assert.True(opened.Document.PrepareApplyCommand.CanExecute(null));

        var pressed = opened.Document.PrepareApplyCommand.ExecuteAsync(null);
        Assert.False(opened.Document.IsConfirmingApply);
        gate.SetResult();
        await pressed;
        await reading;

        Assert.True(opened.Document.IsConfirmingApply);
    }

    [Fact]
    public async Task ASecondReadStartedDuringAReadLeavesTheDocumentSettled()
    {
        await using var opened = await OpenMeasuredPendingAsync();
        var shown = await opened.Fake.ShowProposalAsync(new("project.fwdata", "1.0", "draft/one"), CancellationToken.None);
        var gate = new TaskCompletionSource();
        opened.Fake.ShowProposalHandler = async (_, _) =>
        {
            await gate.Task;
            return shown;
        };
        var first = opened.Document.RefreshAsync();
        var second = opened.Document.RefreshAsync();
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.False(opened.Document.IsLoading);
        Assert.NotNull(opened.Document.Detail);
    }

    [Fact]
    public async Task APressDuringAReadThatNeverEndsReturnsWithAReasonAndCanBePressedAgain()
    {
        await using var opened = await OpenMeasuredPendingAsync();
        var gate = new TaskCompletionSource();
        opened.Fake.ShowProposalHandler = async (_, token) =>
        {
            await gate.Task;
            return default!;
        };
        opened.Document.ReadWait = TimeSpan.FromMilliseconds(200);
        _ = opened.Document.RefreshAsync();

        await opened.Document.PrepareApplyCommand.ExecuteAsync(null);

        Assert.False(opened.Document.IsConfirmingApply);
        Assert.Equal(MotifAppStrings.ProposalApplyStillReading, opened.Document.ApplyRefusal?.Sentence);
        Assert.False(opened.Document.PrepareApplyCommand.IsRunning);
        Assert.True(opened.Document.PrepareApplyCommand.CanExecute(null));
        gate.SetCanceled();
    }

    [Fact]
    public async Task APressWhoseReadEndsOutdatedDoesNotAskForConfirmation()
    {
        await using var opened = await OpenMeasuredPendingAsync();
        opened.Context.Evidence.AppliedSinceRefresh = true;
        await opened.Document.RefreshAsync();

        await opened.Document.PrepareApplyCommand.ExecuteAsync(null);

        Assert.True(opened.Document.IsOutdated);
        Assert.False(opened.Document.IsConfirmingApply);
        Assert.Equal(MotifAppStrings.ProposalDocumentOutdated, opened.Document.ApplyRefusal?.Sentence);
    }

    [Fact]
    public async Task ApplyStaysOffWhileTheChangesAreBeingCheckedSoAPressCannotBeLost()
    {
        await using var opened = await OpenMeasuredPendingAsync();
        var gate = new TaskCompletionSource();
        opened.Fake.MeasurePendingHandler = async (request, _) =>
        {
            await gate.Task;
            return CommandOutcome<MeasurePendingResult>.Success(
                SIL.Motif.Tests.Support.TestFixtures.PendingMeasureResponseFactory.Create("job/pending",
                    request.Revision!, FakeCommandClient.CompleteNumbers));
        };

        var checking = opened.Document.MeasurePendingCommand.ExecuteAsync(null);
        try
        {
            Assert.True(opened.Document.IsCheckingChanges);
            Assert.False(opened.Document.CanPrepareApply);
            Assert.False(opened.Document.PrepareApplyCommand.CanExecute(null));
        }
        finally
        {
            gate.TrySetResult();
            await checking;
        }

        Assert.False(opened.Document.IsCheckingChanges);
        Assert.True(opened.Document.PrepareApplyCommand.CanExecute(null));
        await opened.Document.PrepareApplyCommand.ExecuteAsync(null);
        Assert.True(opened.Document.IsConfirmingApply);
    }

    [Fact]
    public async Task APressThatCannotOpenTheConfirmationSaysWhy()
    {
        await using var opened = await OpenMeasuredPendingAsync();
        opened.Context.ProposalApply.ShowReconciliationNeeded();
        var gate = new TaskCompletionSource();
        var shown = await opened.Fake.ShowProposalAsync(new("project.fwdata", "1.0", "draft/one"), CancellationToken.None);
        opened.Fake.ShowProposalHandler = async (_, _) =>
        {
            await gate.Task;
            return shown;
        };
        _ = opened.Document.RefreshAsync();
        var pressed = opened.Document.PrepareApplyCommand.ExecuteAsync(null);
        gate.SetResult();
        await pressed;

        Assert.False(opened.Document.IsConfirmingApply);
        Assert.False(string.IsNullOrWhiteSpace(opened.Document.ApplyRefusal?.Sentence));
    }

    [Fact]
    public async Task ARefusedApplyShowsItsRefusalWithoutAReceiptOrARefresh()
    {
        await using var opened = await OpenMeasuredPendingAsync();
        opened.Fake.ApplyPendingHandler = (_, _) => Task.FromResult(CommandOutcome<ApplyPendingResult>.Refused(
            new Refusal("apply.refused", FailureReason.Refused, "FieldWorks has the project open.")));
        await opened.Document.PrepareApplyCommand.ExecuteAsync(null);

        await opened.Document.ConfirmApplyCommand.ExecuteAsync(null);

        Assert.Single(opened.Fake.ApplyPendingRequests);
        Assert.False(opened.Document.HasReceipt);
        Assert.Equal("apply.refused", opened.Document.ApplyRefusal?.Code);
        Assert.Equal(0, opened.Refreshes);
        Assert.False(opened.Document.IsConfirmingApply);
        Assert.True(opened.Document.ShowsPrepareApply);
    }

    [Fact]
    public async Task AnAppliedPendingDraftShowsItsReceiptAndRefreshesTheProjectOnce()
    {
        await using var opened = await OpenMeasuredPendingAsync();
        opened.Fake.ApplyPendingHandler = (_, _) =>
        {
            opened.Fake.PendingChangesIs(new(null, "none", [], []));
            return Task.FromResult(CommandOutcome<ApplyPendingResult>.Success(
                ApplyPendingResult.AppliedWith(Receipt, "Saved")));
        };
        await opened.Document.PrepareApplyCommand.ExecuteAsync(null);

        await opened.Document.ConfirmApplyCommand.ExecuteAsync(null);

        Assert.Single(opened.Fake.ApplyPendingRequests);
        Assert.True(opened.Document.HasReceipt);
        Assert.Equal("Receipt for Proposal published/one. Recorded now.", opened.Document.ReceiptText);
        Assert.Equal(1, opened.Refreshes);
    }

    [Fact]
    public async Task ReadingTheDocumentAgainClosesTheApplyQuestion()
    {
        await using var opened = await OpenMeasuredPendingAsync();
        await opened.Document.PrepareApplyCommand.ExecuteAsync(null);
        Assert.True(opened.Document.IsConfirmingApply);

        await opened.Document.RefreshAsync();
        await opened.Document.ConfirmApplyCommand.ExecuteAsync(null);

        Assert.False(opened.Document.IsConfirmingApply);
        Assert.Empty(opened.Fake.ApplyPendingRequests);
    }

    [Fact]
    public async Task ADocumentWhoseEvidenceWentStaleStopsAskingAndCannotBeConfirmed()
    {
        await using var opened = await OpenMeasuredPendingAsync();
        await opened.Document.PrepareApplyCommand.ExecuteAsync(null);

        opened.Document.InvalidateEvidence();
        await opened.Document.ConfirmApplyCommand.ExecuteAsync(null);

        Assert.False(opened.Document.IsConfirmingApply);
        Assert.False(opened.Document.CanApply);
        Assert.False(string.IsNullOrWhiteSpace(opened.Document.ApplyBlockReason));
        Assert.Empty(opened.Fake.ApplyPendingRequests);
    }
}
