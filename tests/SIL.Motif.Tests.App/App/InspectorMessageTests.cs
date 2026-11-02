using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class InspectorMessageTests
{
    [Fact]
    public async Task ARefusedLookupKeepsFoldersOutOfTheMessage()
    {
        var fake = new FakeCommandClient();
        fake.OnInspect((_, _) => Task.FromResult(CommandOutcome<InspectResponse>.Refused(new Refusal(
            "inspect.failed", FailureReason.Refused, "Cannot read /home/alice/Secret Place/store.db now."))));
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("/tmp/inspector-message.fwdata");
        var inspector = new InspectorViewModel(context, _ => "Try a Word");

        context.OpenInspector(InspectorSubject.Morpheme("allomorph-1", null, "a-", null, "authored")!);
        await inspector.Loading;

        Assert.Equal("Cannot read store.db now.", inspector.Message);
    }
}
