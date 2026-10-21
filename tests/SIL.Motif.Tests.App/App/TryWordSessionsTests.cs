using Avalonia.Input;
using SIL.Motif.App;
using SIL.Motif.Commands.Queries;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Try a Word warms the project's parser when the page opens, releases it before the project goes, and shows a quiet
/// loading line only while a submitted word is waiting on the grammar.
/// </summary>
public sealed class TryWordSessionsTests
{
    private const string ProjectA = @"C:\projects\one.fwdata";
    private const string ProjectB = @"C:\projects\two.fwdata";

    [Fact]
    public async Task OpeningTheTryAWordPagePrewarmsTheOpenProjectOnly()
    {
        var (fake, context) = NewContext();
        await context.OpenProjectAsync(ProjectA);
        context.OpenPage(WorkspacePage.Overview);
        Assert.DoesNotContain(fake.TryWordSessionsFake.Calls, call => call.StartsWith("prewarm", StringComparison.Ordinal));

        context.OpenPage(WorkspacePage.TryAWord);

        Assert.Equal("prewarm " + ProjectA, Assert.Single(fake.TryWordSessionsFake.Calls,
            call => call.StartsWith("prewarm", StringComparison.Ordinal)));
    }

    [Fact]
    public void OpeningThePageWithNoProjectPrewarmsNothing()
    {
        var (fake, context) = NewContext();
        context.OpenPage(WorkspacePage.TryAWord);
        Assert.Empty(fake.TryWordSessionsFake.Calls);
    }

    [Fact]
    public async Task LeavingAProjectEvictsItBeforeTheNextOneOpens()
    {
        var (fake, context) = NewContext();
        await context.OpenProjectAsync(ProjectA);
        Assert.Empty(fake.TryWordSessionsFake.Calls);

        await context.OpenProjectAsync(ProjectB);

        Assert.Equal(["evict " + ProjectA], fake.TryWordSessionsFake.Calls);
    }

    [Fact]
    public async Task AFailedOpenEvictsTheProjectItLeaves()
    {
        var fake = new FakeCommandClient();
        fake.OnGetCurrentBaseline((_, _) => Task.FromException<CommandOutcome<CurrentBaselineResponse>>(
            new InvalidOperationException("The project could not be read.")));
        var selection = new SelectionViewModel(fake);
        var workspace = new WorkspaceShellViewModel(new ProjectViewModel(fake, new NoProjectPicker()),
            new BaselineViewModel(fake), selection, new AssessViewModel(fake, selection),
            new FolderPicker(), new DragSource(), fake, tryWordSessions: fake.TryWordSessionsFake);
        var path = Path.Combine(Path.GetTempPath(), "projects", "one.fwdata");

        await workspace.OpenRecentProjectCommand.ExecuteAsync(new RecentProjectViewModel(path));

        Assert.True(workspace.HasOpenRefusal);
        Assert.Null(workspace.Context.ProjectPath);
        Assert.Equal(["evict " + path], fake.TryWordSessionsFake.Calls);
    }

    [Fact]
    public async Task ASubmittedWordShowsLoadingUntilTheGrammarIsReady()
    {
        var fake = new FakeCommandClient();
        var pending = new TaskCompletionSource<CommandOutcome<WordTraceResponse>>();
        fake.OnTraceWord((_, _) => pending.Task);
        var sessions = fake.TryWordSessionsFake;
        sessions.Move(ProjectA, WarmSessionPhase.Loading);
        var trace = new TraceWordViewModel(fake) { WordToTry = "word", TryWordSessions = fake.TryWordSessionsFake };
        trace.SetProjectPath(ProjectA);
        var seen = new List<bool>();
        trace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TraceWordViewModel.IsLoadingGrammar)) seen.Add(trace.IsLoadingGrammar);
        };

        var running = trace.TryCommand.ExecuteAsync(null);
        Assert.True(trace.IsLoadingGrammar);
        Assert.Equal(1, sessions.ListenerCount);

        sessions.Move(ProjectA, WarmSessionPhase.Ready);
        Assert.False(trace.IsLoadingGrammar);
        Assert.True(trace.IsLoading);

        pending.SetResult(CommandOutcome<WordTraceResponse>.Refused(
            new Refusal("test.stop", FailureReason.Cancelled, "stop")));
        await running;
        Assert.Equal([true, false], seen);
        Assert.Equal(0, sessions.ListenerCount);
    }

    [Fact]
    public async Task ALoadingPhaseThatEndsWithTheResultClearsTheLineAndUnsubscribes()
    {
        var fake = new FakeCommandClient();
        var pending = new TaskCompletionSource<CommandOutcome<WordTraceResponse>>();
        fake.OnTraceWord((_, _) => pending.Task);
        fake.TryWordSessionsFake.Move(ProjectA, WarmSessionPhase.Loading);
        var trace = new TraceWordViewModel(fake) { WordToTry = "word", TryWordSessions = fake.TryWordSessionsFake };
        trace.SetProjectPath(ProjectA);

        var running = trace.TryCommand.ExecuteAsync(null);
        Assert.True(trace.IsLoadingGrammar);
        pending.SetResult(CommandOutcome<WordTraceResponse>.Refused(
            new Refusal("test.stop", FailureReason.Cancelled, "stop")));
        await running;

        Assert.False(trace.IsLoadingGrammar);
        Assert.Equal(0, fake.TryWordSessionsFake.ListenerCount);
    }

    [Theory]
    [InlineData(WarmSessionPhase.Idle)]
    [InlineData(WarmSessionPhase.Ready)]
    [InlineData(WarmSessionPhase.Failed)]
    public async Task OnlyTheLoadingPhaseShowsTheLoadingLine(WarmSessionPhase phase)
    {
        var fake = new FakeCommandClient();
        var pending = new TaskCompletionSource<CommandOutcome<WordTraceResponse>>();
        fake.OnTraceWord((_, _) => pending.Task);
        fake.TryWordSessionsFake.Move(ProjectA, phase);
        var trace = new TraceWordViewModel(fake) { WordToTry = "word", TryWordSessions = fake.TryWordSessionsFake };
        trace.SetProjectPath(ProjectA);

        var running = trace.TryCommand.ExecuteAsync(null);
        Assert.False(trace.IsLoadingGrammar);
        // Another project's change does not matter to this word.
        fake.TryWordSessionsFake.Move(ProjectB, WarmSessionPhase.Loading);
        Assert.False(trace.IsLoadingGrammar);

        pending.SetResult(CommandOutcome<WordTraceResponse>.Refused(
            new Refusal("test.stop", FailureReason.Cancelled, "stop")));
        await running;
    }

    [Fact]
    public void TheLoadingTextComesFromTheResx() =>
        Assert.Equal("Loading the grammar…", MotifAppStrings.TryWordLoadingGrammar);

    private static (FakeCommandClient Fake, WorkspaceContext Context) NewContext()
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        var selection = new SelectionViewModel(fake);
        var context = new WorkspaceContext(selection, new AssessViewModel(fake, selection),
            new ChangesViewModel(fake), fake, new FolderPicker(), new DragSource(), new BaselineViewModel(fake),
            tryWordSessions: fake.TryWordSessionsFake);
        return (fake, context);
    }

    private sealed class NoProjectPicker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
