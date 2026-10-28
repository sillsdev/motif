using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.App;

/// <summary>
/// The sample project's shell view model over <see cref="FakeCommandClient"/> and the reader fixture, with no
/// window: for tests whose assertions read only view-model, workspace or fake state. Disposing stops the project's
/// work and removes the reader fixture; it is safe to dispose twice.
/// </summary>
internal sealed class SampleWorkspace : IAsyncDisposable
{
    private readonly StoredSelectionFixture readerFixture;
    private int disposed;

    internal SampleWorkspace(WorkspaceShellViewModel workspace, FakeCommandClient fake, StoredSelectionFixture readerFixture)
    {
        Workspace = workspace;
        Fake = fake;
        this.readerFixture = readerFixture;
    }

    public WorkspaceShellViewModel Workspace { get; }

    public FakeCommandClient Fake { get; }

    /// <summary>Same arguments and meaning as <see cref="PageScreenshots.OpenOverSampleData"/> without the window ones.</summary>
    public static Task<SampleWorkspace> Open(
        bool parse = true, Action<FakeCommandClient, AssessCommandResponse>? configure = null, bool leaveSetupOpen = false,
        TextWordsResponse? capturedWords = null, AssessCommandResponse? capturedAssessment = null,
        bool advancedAiModeEnabled = false, SIL.Motif.Commands.Assistants.IAssistantConnectionService? assistantConnections = null,
        bool activateTextsPage = true, bool seedReaderToken = true) =>
        PageScreenshots.OpenSampleCore(parse, configure, leaveSetupOpen, capturedWords, capturedAssessment,
            advancedAiModeEnabled, assistantConnections, recordSetup: null, composeWindow: false, activateTextsPage,
            seedReaderToken, attachShell: null, settle: null);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await Workspace.Context.StopProjectWorkAsync();
        readerFixture.Dispose();
    }
}
