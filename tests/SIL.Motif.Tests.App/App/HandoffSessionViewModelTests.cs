using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class HandoffSessionViewModelTests
{
    [UiThreadFact]
    public async Task SwitchingProjectsClearsTheWordsAndFilesFromThePreviousHandoff()
    {
        var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
        using var session = new HandoffSessionViewModel(context);
        await session.OnProjectOpenedAsync("first.fwdata", CancellationToken.None);
        session.Handoff.UseWords(["first"]);
        session.Handoff.Files.Add(new HandoffFileViewModel("handoff.md", "/tmp/handoff.md"));
        session.Handoff.State = RunState.Completed;
        Assert.Equal("Write the AI Handoff again", session.HandoffActionText);

        session.OnProjectCleared();
        await session.OnProjectOpenedAsync("second.fwdata", CancellationToken.None);

        Assert.Equal("second.fwdata", session.Handoff.ProjectPath);
        Assert.Null(session.Handoff.ChosenWords);
        Assert.Empty(session.Handoff.Files);
        Assert.Equal("Write the AI Handoff", session.HandoffActionText);
    }
}
