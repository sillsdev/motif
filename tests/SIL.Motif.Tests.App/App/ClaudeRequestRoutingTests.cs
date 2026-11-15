using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ClaudeRequestRoutingTests
{
    [Theory]
    [InlineData("word", "word/é́", "A Word", "  Check this reading.  ", "Check this reading.")]
    [InlineData("warning", "warning/rule/17", "Rule Name", "  ", null)]
    public async Task AskingKeepsTheExactTargetAndPublishesOnlyARequest(
        string kind, string id, string label, string note, string? expectedNote)
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        var panel = new SettingsPageModel(context).Claude;
        await context.OpenProjectAsync("one.fwdata");

        Assert.Equal(MotifAppStrings.RequestAskedStatus,
            await AskAssistantAction.RunAsync(context, new AskAssistantTarget(kind, id, label, note)));

        var recorded = Assert.Single(fake.CreateRequestRequests);
        Assert.Equal((kind, id, label, expectedNote),
            (recorded.TargetKind, recorded.TargetId, recorded.TargetLabel, recorded.Note));
        Assert.Equal(id, Assert.Single(panel.Requests.Rows).Request.TargetId);
        Assert.Empty(context.Changes.Items);
        Assert.Empty(fake.ApplyProposalRequests);
        Assert.Empty(fake.ApplyPendingRequests);
    }

    [Theory]
    [InlineData("proposal")]
    [InlineData("compare")]
    [InlineData("guide")]
    [InlineData("matrix-cell")]
    public async Task UnsupportedTargetsCannotBecomeAWordOrARequest(string kind)
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("one.fwdata");
        Assert.Equal(MotifAppStrings.ClaudeUnsupportedTarget,
            await AskAssistantAction.RunAsync(context, new AskAssistantTarget(kind, "id", "label", null)));
        Assert.Empty(fake.CreateRequestRequests);
    }
}
