using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Commands.Queries;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ApplyRefusalTests
{
    private const string ProjectPath = @"C:\projects\apply-refusal.fwdata";

    [Fact]
    public async Task RefusalKeepsTheChangeAndExplainsWhy()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [new PendingChange("kept", "wordform/kept", "first", "approve", "assessment/one",
                "reading", ["operation/kept"])],
            [new ChangeFit("kept", true, [])]));
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult(
            "job/one", "revision/one", FakeCommandClient.CompleteNumbers));
        fake.ApplyPendingRefusal = new Refusal("apply.project-in-use", FailureReason.Busy,
            "The FieldWorks project is already open.");
        var selection = new SelectionViewModel(fake);
        var context = new WorkspaceContext(selection, new AssessViewModel(fake, selection),
            new ChangesViewModel(fake), fake, new FolderPicker(), new DragSource(),
            new BaselineViewModel(fake));
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await page.MeasureCommand.ExecuteAsync(null);

        await page.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("FieldWorks has this project open. Close it before applying changes.",
            page.ApplyRefusal?.Sentence);
        Assert.Single(context.Changes.Items);
        Assert.False(page.HasReceipt);
        Assert.Single(fake.ApplyPendingRequests);
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
