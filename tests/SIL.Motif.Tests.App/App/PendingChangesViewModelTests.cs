using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class PendingChangesViewModelTests
{
    [Fact]
    public async Task ReloadDisplaysTheDraftWrittenThroughTheCommandClient()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [new PendingChange("change/one", "wordform/one", "word", "approve", "assessment/one",
                "chosen reading", ["operation/one"])],
            [new ChangeFit("change/one", true, [])]));
        var changes = new ChangesViewModel(fake);

        await changes.SetProjectAsync("project.fwdata");

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

        await changes.SetProjectAsync("project.fwdata");

        Assert.Contains("no longer fits", changes.ApplyStatus);
        Assert.Contains("deleted", Assert.Single(changes.Items).FitStatus);
    }

    [Fact]
    public async Task RevisionConflictReloadsTheDraftAndKeepsTheRefusalVisible()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", [], []));
        var changes = new ChangesViewModel(fake);
        await changes.SetProjectAsync("project.fwdata");
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
