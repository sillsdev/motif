using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class UndoAfterProjectSwitchTests
{
    private const string FirstProject = @"C:\projects\first.fwdata";
    private const string SecondProject = @"C:\projects\second.fwdata";

    [Fact]
    public async Task ARemoveResultFromThePreviousProjectCannotReplaceTheNewProjectsChanges()
    {
        var firstSnapshot = Snapshot("draft/first", "first-change", "first-word");
        var secondSnapshot = Snapshot("draft/second", "second-change", "second-word");
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        fake.PendingLoadHandler = (request, _) => Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Success(
            request.FwDataPath == FirstProject ? firstSnapshot : secondSnapshot));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<CommandOutcome<PendingChangesSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.PendingRemoveHandler = (request, _) =>
        {
            Assert.Equal(FirstProject, request.FwDataPath);
            started.SetResult();
            return release.Task;
        };
        var selection = new SelectionViewModel(fake);
        var context = new WorkspaceContext(selection, new AssessViewModel(fake, selection),
            new ChangesViewModel(fake), fake, new FolderPicker(), new DragSource(), new BaselineViewModel(fake));
        await context.OpenProjectAsync(FirstProject);

        var undoing = context.Changes.RemoveCommand.ExecuteAsync(Assert.Single(context.Changes.Items));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await context.OpenProjectAsync(SecondProject);
        release.SetResult(CommandOutcome<PendingChangesSnapshot>.Success(
            new PendingChangesSnapshot("draft/first", "revision/after-undo", [], [])));
        await undoing.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains(fake.PendingLoadRequests, request => request.FwDataPath == SecondProject);
        Assert.Equal("second-change", Assert.Single(context.Changes.Items).ChangeId);
    }

    private static PendingChangesSnapshot Snapshot(string draft, string id, string word)
    {
        var change = new PendingChange(id, "wordform/" + id, word, ChangeKinds.Approve,
            "assessment/one", "reading", ["operation/" + id]);
        return new PendingChangesSnapshot(draft, "revision/one", [change], [new ChangeFit(id, true, [])]);
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) => Task.FromResult(allowedEffects);
    }
}
