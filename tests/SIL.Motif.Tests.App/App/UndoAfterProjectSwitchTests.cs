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
    public async Task UndoAllFromThePreviousProjectCannotRemoveTheNewProjectsChanges()
    {
        var firstSnapshot = Snapshot("draft/first", "first-word", "second-word");
        var secondSnapshot = Snapshot("draft/second", "new-first-word", "new-second-word");
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        fake.PendingLoadHandler = (request, _) => Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Success(
            request.FwDataPath == FirstProject ? firstSnapshot : secondSnapshot));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<CommandOutcome<PendingChangesSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.PendingRemoveHandler = (request, _) =>
        {
            if (request.FwDataPath == FirstProject)
            {
                started.SetResult();
                return release.Task;
            }
            var remaining = secondSnapshot.Changes.Where(change => change.ChangeId != request.ChangeId).ToArray();
            return Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Success(secondSnapshot with
            {
                Revision = "revision/after-undo",
                Changes = remaining,
                FitSummary = secondSnapshot.FitSummary.Where(fit =>
                    remaining.Any(change => change.ChangeId == fit.ChangeId)).ToArray(),
            }));
        };
        var selection = new SelectionViewModel(fake);
        var context = new WorkspaceContext(selection, new AssessViewModel(fake, selection),
            new ChangesViewModel(fake), fake, new FolderPicker(), new DragSource(), new BaselineViewModel(fake));
        var review = new ReviewPageModel(context);
        await context.OpenProjectAsync(FirstProject);

        var undoing = review.ReviewGroups.Single(group => group.Title == "Added")
            .UndoAllCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await context.OpenProjectAsync(SecondProject);
        release.SetResult(CommandOutcome<PendingChangesSnapshot>.Success(
            firstSnapshot with { Revision = "revision/after-undo", Changes = [], FitSummary = [] }));
        await undoing.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains(fake.PendingLoadRequests, request => request.FwDataPath == SecondProject);
        Assert.Equal(FirstProject, Assert.Single(fake.PendingRemoveRequests).FwDataPath);
        Assert.Equal(secondSnapshot.Changes.Select(change => change.ChangeId),
            context.Changes.Snapshot.Changes.Select(change => change.ChangeId));
    }

    private static PendingChangesSnapshot Snapshot(string draft, string firstWord, string secondWord)
    {
        var changes = new[]
        {
            new PendingChange("shared-first", "wordform/shared-first", firstWord, ChangeKinds.AddCandidate,
                "assessment/one", "first reading", ["operation/first"]),
            new PendingChange("shared-second", "wordform/shared-second", secondWord, ChangeKinds.AddCandidate,
                "assessment/one", "second reading", ["operation/second"]),
        };
        return new PendingChangesSnapshot(draft, "revision/one", changes,
            changes.Select(change => new ChangeFit(change.ChangeId, true, [])).ToArray());
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
