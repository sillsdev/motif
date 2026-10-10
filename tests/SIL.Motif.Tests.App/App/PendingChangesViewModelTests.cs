using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class PendingChangesViewModelTests
{
    private const string InternalId = "12345678-1234-1234-1234-123456789abc";

    [Theory]
    [InlineData(ChangeKinds.Approve, true)]
    [InlineData(ChangeKinds.Reject, true)]
    [InlineData(ChangeKinds.Candidate, true)]
    [InlineData(ChangeKinds.IncorrectSpelling, false)]
    [InlineData(ChangeKinds.AddCandidate, false)]
    public async Task RecordActionsPreserveCapturedTargetsAndEvidenceWithoutDisplayedTokens(string kind, bool hasOccurrence)
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");
        changes.AssessmentId = "newer-global-assessment";
        var wordform = Guid.NewGuid();
        var anchor = new OccurrenceAnchor(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 37);
        var expected = new ExpectedContext(new BaselineToken("project", "sha256:" + new string('a', 64),
            "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64))) { TextIds = [anchor.TextId] };
        var target = new WordActionTarget("captured", wordform, anchor, "producing-assessment", expected);
        var reading = new WordActionReading(new ParseAnalysis([new ParseMorph(null, null, null, "frozen")]),
            "frozen reading", 2);

        Assert.True(await changes.AddFromTextAsync(kind, target, reading));

        var request = Assert.Single(fake.PendingPutRequests);
        Assert.Same(expected, request.ExpectedContext);
        Assert.Equal(CanonicalId.FromGuid(wordform).Value, request.Change.WordformId);
        Assert.Equal("captured", request.Change.Word);
        Assert.Equal("producing-assessment", request.Change.AssessmentId);
        Assert.Same(reading.Analysis, request.Change.Reading);
        Assert.Equal(2, request.Change.ReadingIndex);
        Assert.Equal("frozen reading", request.Change.DisplayReading);
        Assert.Equal(hasOccurrence ? anchor : null, request.Change.Occurrence);
    }

    [Fact]
    public async Task ARecordMarkingChoiceKeepsItsTargetWhileTheWindowMovesAndReportsContextRefusal()
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");
        var anchor = new OccurrenceAnchor(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3);
        var expected = new ExpectedContext(new BaselineToken("project", "sha256:" + new string('a', 64),
            "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64))) { TextIds = [anchor.TextId] };
        var target = new WordActionTarget("first", Guid.NewGuid(), anchor, "producing-assessment", expected);
        var action = new AnalysisMarkingAction(AnalysisMarkingActionKind.Approve, "Approve", null,
            new ParseAnalysis([new ParseMorph(null, null, null, "first")]), 0,
            "Unknown", "Approved", ChangeKinds.Approve);
        var completion = new TaskCompletionSource<CommandOutcome<PendingChangesSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.PendingPutHandler = (_, _) => completion.Task;
        var writing = changes.AddFromMarkingAsync(action, target);
        changes.AssessmentId = "different-assessment";
        var refusal = new Refusal("change.context-changed", FailureReason.Refused, "Refresh this Selection first.");
        completion.SetResult(CommandOutcome<PendingChangesSnapshot>.Refused(refusal));

        Assert.False(await writing);

        var request = Assert.Single(fake.PendingPutRequests);
        Assert.Same(expected, request.ExpectedContext);
        Assert.Equal("first", request.Change.Word);
        Assert.Equal("producing-assessment", request.Change.AssessmentId);
        Assert.Equal(anchor, request.Change.Occurrence);
        Assert.Equal(0, request.Change.ReadingIndex);
        Assert.Same(refusal, changes.LastRefusal);
        Assert.Empty(changes.Items);
    }

    [Fact]
    public async Task CollectionReportsReplacementsAndSkippedBulkWords()
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");
        var existing = new PendingChange(InternalId, "wordform", "word", "approve", null, null, ["operation"]);
        fake.PendingPutResponse = new PendingChangesSnapshot("draft", "revision/one", [existing], [])
        {
            ReplacedChangeId = InternalId,
        };

        await changes.PutAsync(new ChangeIntent("newer", "reject", "wordform", "word"));

        Assert.Equal("Replaced an earlier pending change.", changes.CollectionNotice);
        Assert.DoesNotContain(InternalId, changes.CollectionNotice);
        changes.BeginCollection();
        fake.PendingPutResponse = new PendingChangesSnapshot("draft", "revision/one", [], [])
        {
            CancelledChangeId = InternalId,
        };

        await changes.PutAsync(new ChangeIntent("newer", "candidate", "wordform", "word"));

        Assert.Equal("Cancelled the pending choice.", changes.CollectionNotice);
        Assert.DoesNotContain(InternalId, changes.CollectionNotice);
        changes.BeginCollection();
        fake.PendingPutResponse = new PendingChangesSnapshot("draft", "revision/one", [existing], [])
        {
            SkippedWord = "word",
        };

        await changes.PutAsync(new ChangeIntent("bulk", "add-candidate", "wordform", "word"));

        Assert.Equal("Skipped one word because a choice is already pending.", changes.CollectionNotice);
    }

    [Theory]
    [InlineData(ChangeKinds.Approve)]
    [InlineData(ChangeKinds.Reject)]
    [InlineData(ChangeKinds.Candidate)]
    [InlineData(ChangeKinds.AddCandidate)]
    [InlineData(ChangeKinds.IncorrectSpelling)]
    [InlineData(ChangeKinds.RemoveAnalysis)]
    public async Task UndoAndRedoUseTheStagingCommandsForEveryChangeKind(string kind)
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");
        var intent = new ChangeIntent("change/one", kind, "wordform/one", "word");

        await changes.PutAsync(intent);

        Assert.Equal("Undid the change for word.", await changes.UndoStagingActionAsync());
        Assert.Equal("change/one", Assert.Single(fake.PendingRemoveRequests).ChangeId);
        Assert.Empty(changes.Items);

        Assert.Equal("Redid the change for word.", await changes.RedoStagingActionAsync());
        Assert.Equal(2, fake.PendingPutRequests.Count);
        Assert.Equal(intent, fake.PendingPutRequests.Last().Change);
        Assert.Equal(kind, Assert.Single(changes.Items).Kind);
    }

    [Fact]
    public async Task OneStagingActionCanUndoAndRedoSeveralChangesTogether()
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");
        using (changes.BeginStagingAction())
        {
            await changes.PutAsync(new ChangeIntent("change/one", ChangeKinds.Approve, "wordform/one", "one"));
            await changes.PutAsync(new ChangeIntent("change/two", ChangeKinds.IncorrectSpelling, "wordform/two", "two"));
        }

        Assert.Equal("Undid changes to 2 words.", await changes.UndoStagingActionAsync());
        Assert.Equal(2, fake.PendingRemoveRequests.Count);
        Assert.Empty(changes.Items);
        Assert.Equal("Redid changes to 2 words.", await changes.RedoStagingActionAsync());
        Assert.Equal(4, fake.PendingPutRequests.Count);
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public async Task UndoRefusesWhenAnotherWriterChangedThePendingChange()
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");
        await changes.PutAsync(new ChangeIntent("change/one", ChangeKinds.Approve, "wordform/one", "word"));
        var current = Assert.Single(changes.Snapshot.Changes);
        fake.PendingChangesIs(changes.Snapshot with { Changes = [current with { Word = "changed elsewhere" }] });

        var status = await changes.UndoStagingActionAsync();

        Assert.Contains("changed outside this window", status, StringComparison.Ordinal);
        Assert.Empty(fake.PendingRemoveRequests);
        Assert.Equal("changed elsewhere", Assert.Single(changes.Items).Word);
    }

    [Fact]
    public async Task RemovingAChangeCanBeUndoneThroughThePutCommand()
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");
        await changes.PutAsync(new ChangeIntent("change/one", ChangeKinds.Approve, "wordform/one", "word"));
        await changes.RemoveCommand.ExecuteAsync(Assert.Single(changes.Items));

        Assert.Equal("Undid the change for word.", await changes.UndoStagingActionAsync());
        Assert.Equal(2, fake.PendingPutRequests.Count);
        Assert.Equal("change/one", Assert.Single(changes.Items).ChangeId);
    }

    [Fact]
    public async Task NewStagingActionClearsRedoHistory()
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");
        await changes.PutAsync(new ChangeIntent("change/one", ChangeKinds.Approve, "wordform/one", "one"));
        await changes.UndoStagingActionAsync();
        await changes.PutAsync(new ChangeIntent("change/two", ChangeKinds.Reject, "wordform/two", "two"));

        Assert.Equal("There is nothing to redo.", await changes.RedoStagingActionAsync());
        Assert.Equal("change/two", Assert.Single(changes.Items).ChangeId);
    }

    [Fact]
    public async Task UndoAllIsOneUndoableAndRedoableStagingAction()
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");
        await changes.PutAsync(new ChangeIntent("change/one", ChangeKinds.Approve, "wordform/one", "one"));
        await changes.PutAsync(new ChangeIntent("change/two", ChangeKinds.Reject, "wordform/two", "two"));
        var group = new ReviewChangeGroupViewModel("Opinions", changes.Items.ToArray(), changes);

        await group.UndoAllCommand.ExecuteAsync(null);

        Assert.Empty(changes.Items);
        Assert.Equal("Undid changes to 2 words.", await changes.UndoStagingActionAsync());
        Assert.Equal(2, changes.Count);
        Assert.Equal("Redid changes to 2 words.", await changes.RedoStagingActionAsync());
        Assert.Empty(changes.Items);
        Assert.Equal(4, fake.PendingRemoveRequests.Count);
        Assert.Equal(4, fake.PendingPutRequests.Count);
    }

    [Fact]
    public async Task BulkCandidatesSendEveryParserReadingWithItsAssessmentIndex()
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");
        changes.AssessmentId = "assessment/one";
        var assessmentWord = new AssessmentWordResult("word", "analysed", false, "Done", 1, null)
        {
            Morphology = new ParseWordEvidence("v1", 0, "word", 1, false, false, false,
                [new ParseAnalysis([new ParseMorph(null, null, null, "first")]),
                 new ParseAnalysis([new ParseMorph(null, null, null, "second")])], []),
        };
        var row = new AssessWordRowViewModel(assessmentWord);
        var word = new CompareWordViewModel(row, (WordProjectStatus.NotPresent, CompareColumnKind.NoMatch));

        await changes.AddAsync(ChangeKinds.AddCandidate, word);

        Assert.Equal([0, 1], fake.PendingPutRequests.Select(request => request.Change.ReadingIndex));
        Assert.All(fake.PendingPutRequests, request => Assert.Equal("assessment/one", request.Change.AssessmentId));
        Assert.Equal(2, changes.Items.Count);
        Assert.Equal("second", fake.PendingPutRequests.Last().Change.Reading!.Morphs.Single().GuessedString);
    }

    [Fact]
    public async Task ApprovingAStoredAnalysisUsesItsIdentityWithoutAParserReading()
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");

        await changes.ApproveStoredAnalysisAsync("word", "analysis/one", "form = gloss",
            WorkspacePage.TryAWord);

        var change = Assert.Single(fake.PendingPutRequests).Change;
        Assert.Equal(ChangeKinds.Approve, change.Kind);
        Assert.Equal("word", change.Word);
        Assert.Equal("analysis/one", change.StoredAnalysisId);
        Assert.Null(change.AssessmentId);
        Assert.Null(change.Reading);
        Assert.Null(change.ReadingIndex);
        Assert.Null(change.Occurrence);
        Assert.Equal(WorkspacePage.TryAWord.ToString(), change.OriginPage);
    }

    [Fact]
    public async Task RemovingAStoredAnalysisUsesTheCommandAndPublishesARemovedChange()
    {
        var removed = new PendingChange("change/remove", "wordform/one", "kitabu", ChangeKinds.RemoveAnalysis,
            null, "reading", ["operation/remove"])
        {
            StoredAnalysisId = "analysis/one",
        };
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", [], []));
        fake.RemoveAnalysisCompletesWith(new PendingChangesSnapshot("draft/one", "revision/removed", [removed],
            [new ChangeFit(removed.ChangeId, true, [])]));
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");

        await changes.RemoveAnalysisAsync("wordform/one", "kitabu", "analysis/one");

        var request = Assert.Single(fake.RemoveAnalysisRequests);
        Assert.Equal("project.fwdata", request.FwDataPath);
        Assert.Equal("revision/one", request.ExpectedRevision);
        Assert.Equal("wordform/one", request.WordformId);
        Assert.Equal("kitabu", request.Word);
        Assert.Equal("analysis/one", request.AnalysisId);
        Assert.False(string.IsNullOrWhiteSpace(request.ChangeId));
        Assert.Equal(ChangeKinds.RemoveAnalysis, Assert.Single(changes.Items).Kind);
    }

    [Fact]
    public async Task AcceptingASetUsesItsTextScopeAndPublishesOneUndoGroup()
    {
        var textId = Guid.Parse("00000001-0000-0000-0000-000000000001");
        var first = new PendingChange("change/one", "wordform/one", "kitabu", ChangeKinds.AddCandidate,
            "assessment/one", "first reading", ["operation/one"]) { GroupId = "group/accepted" };
        var second = new PendingChange("change/two", "wordform/two", "kitabu cha", ChangeKinds.AddCandidate,
            "assessment/one", "second reading", ["operation/two"]) { GroupId = "group/accepted" };
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", [], []));
        fake.AcceptNewSetCompletesWith(new PendingChangesSnapshot("draft/one", "revision/accepted", [first, second],
            [new ChangeFit(first.ChangeId, true, []), new ChangeFit(second.ChangeId, true, [])]));
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");

        await changes.AcceptNewSetAsync("assessment/one", textId: textId);

        var request = Assert.Single(fake.AcceptNewSetRequests);
        Assert.Equal("project.fwdata", request.FwDataPath);
        Assert.Equal("revision/one", request.ExpectedRevision);
        Assert.Equal("assessment/one", request.AssessmentId);
        Assert.Equal(textId, request.TextId);
        Assert.False(request.Selection);
        Assert.Equal(["Added as Unknown from accepting a set", "Added as Unknown from accepting a set"],
            changes.Items.Select(item => item.SourceText));
        Assert.All(changes.Items, change => Assert.Equal("group/accepted", change.GroupId));
    }

    [Fact]
    public async Task WordAndCompareChangesDoNotCarryAnOccurrenceAnchor()
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");
        var analysis = new ParseAnalysis([new ParseMorph(null, null, null, "reading")]);
        var assessmentWord = new AssessmentWordResult("word", "analysed", false, "Done", 1, null)
        {
            Morphology = new ParseWordEvidence("v1", 0, "word", 1, false, false, false, [analysis], []),
        };
        var word = new CompareWordViewModel(new AssessWordRowViewModel(assessmentWord),
            (WordProjectStatus.NotPresent, CompareColumnKind.NoMatch));

        await changes.AddAsync(ChangeKinds.AddCandidate, word, WorkspacePage.Texts);
        await changes.AddAsync(ChangeKinds.IncorrectSpelling, word, WorkspacePage.Texts);

        Assert.Equal(2, fake.PendingPutRequests.Count);
        Assert.All(fake.PendingPutRequests, request => Assert.Null(request.Change.Occurrence));
    }

    [Fact]
    public async Task ARefusedCandidateDoesNotHideLaterParserReadings()
    {
        var fake = new FakeCommandClient
        {
            PendingPutRefusal = new Refusal("change.cannot-compose", FailureReason.Refused,
                "The reading is already stored."),
            PendingPutRefusalOnCall = 1,
        };
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");
        var result = new AssessmentWordResult("word", "analysed", false, "Done", 1, null)
        {
            Morphology = new ParseWordEvidence("v1", 0, "word", 1, false, false, false,
                [new ParseAnalysis([new ParseMorph(null, null, null, "first")]),
                 new ParseAnalysis([new ParseMorph(null, null, null, "second")])], []),
        };
        var word = new CompareWordViewModel(new AssessWordRowViewModel(result),
            (WordProjectStatus.NotPresent, CompareColumnKind.NoMatch));

        await changes.AddAsync(ChangeKinds.AddCandidate, word);

        Assert.Equal(2, fake.PendingPutRequests.Count);
        Assert.Single(changes.Items);
        Assert.Equal("change.cannot-compose", changes.LastRefusal?.Code);
    }

    [Fact]
    public async Task ReloadDisplaysTheDraftWrittenThroughTheCommandClient()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [new PendingChange("change/one", "wordform/one", "word", "approve", "assessment/one",
                "chosen reading", ["operation/one"])],
            [new ChangeFit("change/one", true, [])]));
        var changes = new ChangesViewModel(fake);

        await changes.OpenProjectAsync("project.fwdata");

        Assert.Equal("change/one", Assert.Single(changes.Items).ChangeId);
        Assert.Equal("revision/one", changes.Snapshot.Revision);
    }

    [Fact]
    public async Task UndoingOneAcceptedChoiceKeepsTheOtherReading()
    {
        const string groupId = "accept/group";
        var first = new PendingChange("change/one", "wordform/one", "one", "add-candidate", "assessment/one",
            "first", ["operation/one"]) { GroupId = groupId };
        var second = new PendingChange("change/two", "wordform/two", "two", "add-candidate", "assessment/one",
            "second", ["operation/two"]) { GroupId = groupId };
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", [first, second],
            [new ChangeFit(first.ChangeId, true, []), new ChangeFit(second.ChangeId, true, [])]));
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");

        await changes.RemoveCommand.ExecuteAsync(changes.Items[0]);

        Assert.Equal(first.ChangeId, Assert.Single(fake.PendingRemoveRequests).ChangeId);
        Assert.Equal(second.ChangeId, Assert.Single(changes.Items).ChangeId);
    }

    [Fact]
    public async Task AStaleChangeIsVisibleAndBlocksReview()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/two",
            [new PendingChange("change/two", "wordform/two", "word", "approve", "assessment/two",
                "chosen reading", ["operation/two"])],
            [new ChangeFit("change/two", false, [$"Wordform {InternalId} was deleted."])]));
        var changes = new ChangesViewModel(fake);
        var selection = new SelectionViewModel(fake);
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        var context = new WorkspaceContext(selection, new AssessViewModel(fake, selection), changes, fake,
            new FolderPicker(), new DragSource(), new BaselineViewModel(fake));
        var review = new ReviewPageModel(context);

        await context.OpenProjectAsync("project.fwdata");

        Assert.False(Assert.Single(changes.Snapshot.FitSummary).StillFits);
        Assert.False(review.CanApply);
        Assert.Contains("no longer fits", review.ApplyBlockReason);
        Assert.False(review.ApplyCommand.CanExecute(null));
        Assert.Equal("No longer fits the current project. Remove this change before review.",
            Assert.Single(changes.Items).FitStatus);
        Assert.DoesNotContain(InternalId, Assert.Single(changes.Items).FitStatus);
    }

    [Fact]
    public async Task ARefusalShowsMappedTextWithoutInternalTermsOrIds()
    {
        var fake = new FakeCommandClient
        {
            PendingPutRefusal = new Refusal("change.cannot-compose", FailureReason.Refused,
                $"The pending Draft change {InternalId} could not be composed."),
        };
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");

        await changes.PutAsync(new ChangeIntent("change/mine", "reject", "wordform/one", "word"));

        Assert.Equal("This change could not be added. Refresh the changes and try again.", changes.ShownRefusal?.Sentence);
        Assert.DoesNotContain("Draft", changes.ShownRefusal?.Sentence);
        Assert.DoesNotContain(InternalId, changes.ShownRefusal?.Sentence);
    }

    [Fact]
    public async Task RevisionConflictReloadsTheDraftAndKeepsTheRefusalVisible()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", [], []));
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/two",
            [new PendingChange("change/other", "wordform/one", "word", "approve", null, null,
                ["operation/other"])], [new ChangeFit("change/other", true, [])]));
        fake.PendingPutRefusal = new Refusal("change.revision-conflict", FailureReason.Refused,
            "The pending Draft changed.");

        await changes.PutAsync(new ChangeIntent("change/mine", "incorrect-spelling", "", "word"));

        Assert.Equal("revision/two", changes.Snapshot.Revision);
        Assert.Equal("change/other", Assert.Single(changes.Items).ChangeId);
        Assert.Equal("change.revision-conflict", changes.LastRefusal?.Code);
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            Avalonia.Input.PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths,
            DragDropEffects allowedEffects) => Task.FromResult(allowedEffects);
    }
}
