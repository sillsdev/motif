using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>The Applied receipt says who applied, copies the id, opens Texts and says when FieldWorks shows it.</summary>
public sealed class ProposalReceiptTests
{
    private static ApplyProjection Receipt(bool alreadyApplied = false) => new("AaH1Zma5FdfY2eXzcMCOWg", alreadyApplied,
        "Saved", [], "effect", new AppliedLogEntrySummary("AaH1Zma5FdfY2eXzcMCOWg", "20261119T210505Z", "Person", "intent/one"));

    private static async Task<(RecordingClipboard Clipboard, WorkspaceContext Context, OpenProposalDocuments Documents,
        ProposalDocumentViewModel Document)> AppliedAsync(ApplyProjection receipt)
    {
        var fake = new FakeCommandClient();
        var change = new PendingChange("one", "wordform/one", "one", "approve", "assessment/one", "reading", ["op"]);
        fake.PendingChangesIs(new("draft/one", "revision/one", [change], [new ChangeFit("one", true, [])]));
        fake.MeasurePendingHandler = (request, _) => Task.FromResult(CommandOutcome<MeasurePendingResult>.Success(
            SIL.Motif.Tests.Support.TestFixtures.PendingMeasureResponseFactory.Create("job/pending", request.Revision!,
                FakeCommandClient.CompleteNumbers)));
        fake.ApplyPendingHandler = (_, _) =>
        {
            fake.PendingChangesIs(new(null, "none", [], []));
            return Task.FromResult(CommandOutcome<ApplyPendingResult>.Success(ApplyPendingResult.AppliedWith(receipt, "Saved")));
        };
        var clipboard = new RecordingClipboard();
        var context = WorkspaceContextTests.NewContext(fake, clipboard: clipboard);
        await context.OpenProjectAsync("project.fwdata");
        var documents = new OpenProposalDocuments(context);
        var document = documents.Open("draft/one");
        document.Card = new ProposalCardViewModel(new("draft/one", "Draft", "proposed", ProposalGroup.NotAppliedYet,
            true, [], TrialStanding.NotTried, null, []) { IsDraft = true });
        await document.RefreshAsync();
        await document.MeasurePendingCommand.ExecuteAsync(null);
        await document.PrepareApplyCommand.ExecuteAsync(null);
        await document.ConfirmApplyCommand.ExecuteAsync(null);
        return (clipboard, context, documents, document);
    }

    [Fact]
    public async Task TheReceiptSaysAppliedByYouWithLocalTimeAndNoRawId()
    {
        var (_, context, documents, document) = await AppliedAsync(Receipt());
        try
        {
            Assert.True(document.HasReceipt);
            Assert.StartsWith("Applied by you. Recorded ", document.ReceiptText, StringComparison.Ordinal);
            Assert.DoesNotContain("AaH1Zma5FdfY2eXzcMCOWg", document.ReceiptText, StringComparison.Ordinal);
            Assert.DoesNotContain("20261119T210505Z", document.ReceiptText, StringComparison.Ordinal);
            Assert.Equal("FieldWorks shows this change the next time you open the project.", document.ReceiptFieldWorksNote);
        }
        finally { await documents.StopAsync(); await context.StopProjectWorkAsync(); }
    }

    [Fact]
    public async Task AReceiptForAProposalThatWasAlreadyAppliedDoesNotClaimThePersonAppliedIt()
    {
        var (_, context, documents, document) = await AppliedAsync(Receipt(alreadyApplied: true));
        try
        {
            Assert.StartsWith("Applied. Recorded ", document.ReceiptText, StringComparison.Ordinal);
        }
        finally { await documents.StopAsync(); await context.StopProjectWorkAsync(); }
    }

    [Fact]
    public async Task CopyReceiptIdPutsTheIdOnTheClipboardAndSaysSo()
    {
        var (clipboard, context, documents, document) = await AppliedAsync(Receipt());
        try
        {
            Assert.Equal("Copy Receipt ID", document.ReceiptCopyLabel);

            await document.CopyReceiptIdCommand.ExecuteAsync(null);

            Assert.Equal(["AaH1Zma5FdfY2eXzcMCOWg"], clipboard.Copied);
            Assert.Equal("Copied", document.ReceiptCopyLabel);
        }
        finally { await documents.StopAsync(); await context.StopProjectWorkAsync(); }
    }

    [Fact]
    public async Task ACopyThatFailsSaysSoInsteadOfLookingDone()
    {
        var (clipboard, context, documents, document) = await AppliedAsync(Receipt());
        try
        {
            clipboard.Failure = new InvalidOperationException("no clipboard");

            await document.CopyReceiptIdCommand.ExecuteAsync(null);

            Assert.Empty(clipboard.Copied);
            Assert.Equal("Could Not Copy", document.ReceiptCopyLabel);
        }
        finally { await documents.StopAsync(); await context.StopProjectWorkAsync(); }
    }

    [Fact]
    public async Task SeeInTextsOpensTheTextsPage()
    {
        var (_, context, documents, document) = await AppliedAsync(Receipt());
        try
        {
            Assert.True(document.SeeInTextsCommand.CanExecute(null));

            document.SeeInTextsCommand.Execute(null);

            Assert.Equal(WorkspacePage.Texts, context.CurrentPage);
        }
        finally { await documents.StopAsync(); await context.StopProjectWorkAsync(); }
    }
}
