using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class PendingChangesViewModelTests
{
    private const string InternalId = "12345678-1234-1234-1234-123456789abc";

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

        word.SelectedReading = word.ReadingChoices[1];
        await changes.AddAsync(ChangeKinds.Approve, word);
        Assert.Equal(1, fake.PendingPutRequests.Last().Change.ReadingIndex);
        Assert.Equal("second", fake.PendingPutRequests.Last().Change.Reading!.Morphs.Single().GuessedString);
    }

    [Fact]
    public async Task ApprovingAStoredAnalysisUsesItsIdentityWithoutAParserReading()
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        await changes.SetProjectAsync("project.fwdata");

        await changes.ApproveStoredAnalysisAsync("word", "analysis/one", "form = gloss",
            WorkspacePage.TryAWord);

        var change = Assert.Single(fake.PendingPutRequests).Change;
        Assert.Equal(ChangeKinds.Approve, change.Kind);
        Assert.Equal("word", change.Word);
        Assert.Equal("analysis/one", change.StoredAnalysisId);
        Assert.Null(change.AssessmentId);
        Assert.Null(change.Reading);
        Assert.Null(change.ReadingIndex);
        Assert.Equal(WorkspacePage.TryAWord.ToString(), change.OriginPage);
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
    public async Task AStaleChangeIsVisibleAndBlocksReview()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/two",
            [new PendingChange("change/two", "wordform/two", "word", "approve", "assessment/two",
                "chosen reading", ["operation/two"])],
            [new ChangeFit("change/two", false, [$"Wordform {InternalId} was deleted."])]));
        var changes = new ChangesViewModel(fake);

        await changes.OpenProjectAsync("project.fwdata");

        Assert.False(Assert.Single(changes.Snapshot.FitSummary).StillFits);
        Assert.Contains("no longer fits", changes.ApplyStatus);
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

        Assert.Equal("This change could not be added. Refresh the changes and try again.", changes.ErrorText);
        Assert.DoesNotContain("Draft", changes.ErrorText);
        Assert.DoesNotContain(InternalId, changes.ErrorText);
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
}
