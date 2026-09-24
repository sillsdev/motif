using SIL.Motif.App.ViewModels;
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
}
