using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class PendingChangesViewModelTests
{
    [Fact]
    public async Task CollectionReportsReplacementsAndSkippedBulkWords()
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");
        var existing = new PendingChange("older", "wordform", "word", "approve", null, null, ["operation"]);
        fake.PendingPutResponse = new PendingChangesSnapshot("draft", "revision/one", [existing], [])
        {
            ReplacedChangeId = "older",
        };

        await changes.PutAsync(new ChangeIntent("newer", "reject", "wordform", "word"));

        Assert.Contains("Replaced pending change older", changes.CollectionNotice);
        fake.PendingPutResponse = new PendingChangesSnapshot("draft", "revision/one", [], [])
        {
            CancelledChangeId = "older",
        };

        await changes.PutAsync(new ChangeIntent("newer", "candidate", "wordform", "word"));

        Assert.Contains("Cancelled pending change older", changes.CollectionNotice);
        fake.PendingPutResponse = new PendingChangesSnapshot("draft", "revision/one", [existing], [])
        {
            SkippedWord = "word",
        };

        await changes.PutAsync(new ChangeIntent("bulk", "add-candidate", "wordform", "word"));

        Assert.Contains("Skipped word", changes.CollectionNotice);
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
            [new ChangeFit("change/two", false, ["Wordform was deleted."])]));
        var changes = new ChangesViewModel(fake);

        await changes.OpenProjectAsync("project.fwdata");

        Assert.False(Assert.Single(changes.Snapshot.FitSummary).StillFits);
        Assert.Contains("deleted", Assert.Single(changes.Items).FitStatus);
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
