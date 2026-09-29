using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ReviewChangeGroupsTests
{
    private const string ProjectPath = @"C:\projects\markings.fwdata";

    [Fact]
    public async Task GroupsFollowTheDesignedOrderAndUseTheDesignedTitles()
    {
        var changes = new[]
        {
            Change("uncertain", "uncertain", ChangeKinds.Approve),
            Change("spelling", "spelling", ChangeKinds.IncorrectSpelling),
            Change("removed", "removed", "remove-analysis", ReadingGrade.Disapproved),
            Change("accepted", "accepted", ChangeKinds.AddCandidate, groupId: "accepted-set"),
            Change("added", "added", ChangeKinds.AddCandidate),
            Change("down-to-unknown", "down-to-unknown", ChangeKinds.Candidate, ReadingGrade.Disapproved),
            Change("down-to-approved", "down-to-approved", ChangeKinds.Approve, ReadingGrade.Disapproved),
            Change("approved-down", "approved-down", ChangeKinds.Reject, ReadingGrade.Approved),
            Change("up-to-unknown", "up-to-unknown", ChangeKinds.Candidate, ReadingGrade.Approved),
            Change("unknown-down", "unknown-down", ChangeKinds.Reject),
            Change("unknown-up", "unknown-up", ChangeKinds.Approve),
        };
        var (page, _) = await OpenReviewAsync(changes, ["uncertain"]);

        Assert.Equal(
        [
            "Unknown → Approved",
            "Unknown → Disapproved",
            "Approved → Disapproved",
            "Approved → Unknown",
            "Disapproved → Approved",
            "Disapproved → Unknown",
            "Added as Unknown",
            "Removed",
            "Spelling → Incorrect",
            "Uncertain — check again",
        ], page.ReviewGroups.Select(group => group.Title));
        Assert.Equal("Unknown → Approved", Assert.Single(page.ReviewGroups[^1].Items).TransitionText);
    }

    [Fact]
    public async Task ItemsAreSortedByTextSentenceAndWord()
    {
        var firstText = Guid.Parse("00000001-0000-0000-0000-000000000000");
        var secondText = Guid.Parse("00000002-0000-0000-0000-000000000000");
        var paragraph = Guid.Parse("00000003-0000-0000-0000-000000000000");
        var firstSentence = Guid.Parse("00000004-0000-0000-0000-000000000000");
        var secondSentence = Guid.Parse("00000005-0000-0000-0000-000000000000");
        var changes = new[]
        {
            Change("later-text", "a", ChangeKinds.Approve,
                occurrence: new OccurrenceAnchor(secondText, paragraph, firstSentence, 0)),
            Change("later-word", "a", ChangeKinds.Approve,
                occurrence: new OccurrenceAnchor(firstText, paragraph, firstSentence, 2)),
            Change("later-sentence", "a", ChangeKinds.Approve,
                occurrence: new OccurrenceAnchor(firstText, paragraph, secondSentence, 0)),
            Change("first-word", "z", ChangeKinds.Approve,
                occurrence: new OccurrenceAnchor(firstText, paragraph, firstSentence, 1)),
        };
        var (page, _) = await OpenReviewAsync(changes);

        Assert.Equal(["first-word", "later-word", "later-sentence", "later-text"],
            Assert.Single(page.ReviewGroups).Items.Select(change => change.ChangeId));
    }

    [Fact]
    public async Task AddedItemsNameWhetherTheyCameFromAnAddOrAnAcceptedSet()
    {
        var (page, _) = await OpenReviewAsync(
        [
            Change("accepted", "accepted", ChangeKinds.AddCandidate, groupId: "accepted-set"),
            Change("added", "added", ChangeKinds.AddCandidate),
        ]);

        var group = Assert.Single(page.ReviewGroups);
        Assert.Equal("Added as Unknown", group.Title);
        Assert.Equal("Add", group.Items.Single(item => item.ChangeId == "added").SourceText);
        Assert.Equal("Accepting a set", group.Items.Single(item => item.ChangeId == "accepted").SourceText);
    }

    [Fact]
    public async Task UndoAllRemovesEveryChangeInTheGroupOneAtATime()
    {
        var (page, fake) = await OpenReviewAsync(
        [
            Change("first", "first", ChangeKinds.AddCandidate),
            Change("second", "second", ChangeKinds.AddCandidate),
            Change("other", "other", ChangeKinds.IncorrectSpelling),
        ]);

        var group = page.ReviewGroups.Single(item => item.Title == "Added as Unknown");
        await group.UndoAllCommand.ExecuteAsync(null);

        Assert.Equal(["first", "second"], fake.PendingRemoveRequests.Select(request => request.ChangeId));
        Assert.Equal("other", Assert.Single(page.Context.Changes.Items).ChangeId);
    }

    [Fact]
    public async Task UndoAllRemovesAnAcceptedSetByItsSharedGroupIdOnce()
    {
        var (page, fake) = await OpenReviewAsync(
        [
            Change("accepted-one", "one", ChangeKinds.AddCandidate, groupId: "accepted-set"),
            Change("accepted-two", "two", ChangeKinds.AddCandidate, groupId: "accepted-set"),
            Change("added", "added", ChangeKinds.AddCandidate),
        ]);

        var group = page.ReviewGroups.Single(item => item.Title == "Added as Unknown");
        await group.UndoAllCommand.ExecuteAsync(null);

        Assert.Equal(["added", "accepted-set"], fake.PendingRemoveRequests.Select(request => request.ChangeId));
        Assert.Empty(page.Context.Changes.Items);
    }

    [Fact]
    public async Task UncertainActionsUseWordSpecificAccessibleNames()
    {
        var (page, _) = await OpenReviewAsync(
        [
            Change("one", "kitabu", ChangeKinds.Approve),
            Change("two", "kitabu cha", ChangeKinds.Approve),
        ], ["one", "two"]);

        var changes = Assert.Single(page.ReviewGroups).Items;
        Assert.Equal(["Check again: kitabu", "Check again: kitabu cha"],
            changes.Select(change => change.CheckAgainAutomationName));
        Assert.Equal(["Undo: kitabu", "Undo: kitabu cha"], changes.Select(change => change.UndoAutomationName));
    }

    private static async Task<(ReviewPageModel Page, FakeCommandClient Client)> OpenReviewAsync(
        IReadOnlyList<PendingChange> changes, string[]? uncertainChangeIds = null)
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", changes,
            changes.Select(change => new ChangeFit(change.ChangeId,
                uncertainChangeIds?.Contains(change.ChangeId) == true
                    ? ChangeFitStatus.Uncertain : ChangeFitStatus.Fits, [])).ToArray()));
        var selection = new SelectionViewModel(fake);
        var context = new WorkspaceContext(selection, new AssessViewModel(fake, selection),
            new ChangesViewModel(fake), fake, new FolderPicker(), new DragSource(), new BaselineViewModel(fake));
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        return (page, fake);
    }

    private static PendingChange Change(string id, string word, string kind,
        string opinion = ReadingGrade.Candidate, OccurrenceAnchor? occurrence = null, string? groupId = null)
    {
        var stored = kind is ChangeKinds.Approve or ChangeKinds.Reject or ChangeKinds.Candidate or "remove-analysis";
        return new PendingChange(id, "wordform/" + id, word, kind, null, word, ["operation/" + id])
        {
            Analyses = stored
                ? [new ReviewAnalysis(new ParserReading([]), opinion, true, true)]
                : [],
            OriginPage = WorkspacePage.Texts.ToString(),
            Occurrence = occurrence,
            StoredAnalysisId = stored ? "analysis/" + id : null,
            GroupId = groupId,
        };
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
