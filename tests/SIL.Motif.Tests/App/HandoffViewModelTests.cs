using System.ComponentModel;
using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins <see cref="HandoffViewModel"/>'s run state machine — the same shape as <see cref="AssessViewModel"/>'s
/// — plus what is specific to Handoff: backing out of the destination picker never starts a run, a
/// completed run's files become draggable rows only once each is verified to sit inside its own output
/// directory, the data-sensitivity sentence is the instructions asset's own wording, and the drag adapter
/// receives the exact existing path of one file or of every file.
/// </summary>
public sealed class HandoffViewModelTests
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string BundleDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static (FakeCommandClient Fake, FakeDragSource DragSource, HandoffViewModel Handoff) NewViewModel(
        string? folder = @"C:\out")
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        selection.AllWordforms = true;
        var dragSource = new FakeDragSource();
        var handoff = new HandoffViewModel(fake, selection, new FakeFolderPicker(folder), dragSource)
        {
            ProjectPath = ProjectPath,
            InvocationId = "invocation/one",
        };
        return (fake, dragSource, handoff);
    }

    private static HandoffCommandResponse NewResponse(string outputDirectory, params string[] files) => new(
        outputDirectory,
        new BaselineCaptureResponse(
            new BaselineToken("project-1", Digest, "1", "2026-09-05T00:00:00Z", BundleDigest),
            ProjectPath, DateTimeOffset.UtcNow, FieldWorksHeldProject: false, ReusedExistingBytes: true),
        new SelectionProjection([], []),
        files,
        ["assessment/one"]);

    private static HandoffCommandResponse NewResponseWithHeader(
        string outputDirectory, string pastedHeader, string handoffMarkdown) =>
        NewResponse(outputDirectory) with { PastedHeader = pastedHeader, HandoffMarkdown = handoffMarkdown };

    [Fact]
    public async Task BackingOutOfTheFolderDialogLeavesStateIdleAndNeverCallsHandoff()
    {
        var (fake, _, handoff) = NewViewModel(folder: null);

        await handoff.RunCommand.ExecuteAsync(null);

        Assert.Empty(fake.HandoffRequests);
        Assert.Equal(RunState.Idle, handoff.State);
        Assert.Null(handoff.OutputDirectory);
        Assert.Empty(handoff.Files);
    }

    [Fact]
    public void RunIsDisabledUntilACompletedAssessmentIsSelected()
    {
        var (fake, _, handoff) = NewViewModel();
        handoff.InvocationId = null;

        Assert.False(handoff.RunCommand.CanExecute(null));

        handoff.InvocationId = "invocation/one";
        Assert.True(handoff.RunCommand.CanExecute(null));
    }

    [Fact]
    public void ResetClearsTheSelectedInvocation()
    {
        var (_, _, handoff) = NewViewModel();

        handoff.Reset();

        Assert.Null(handoff.InvocationId);
        Assert.False(handoff.RunCommand.CanExecute(null));
    }

    [Fact]
    public async Task RunningWritesTheDestinationAndExposesEveryFileAsADraggableRow()
    {
        var (fake, _, handoff) = NewViewModel();
        var response = NewResponse(@"C:\out", "grammar.json", "texts/one.flextext.json");
        fake.HandoffCompletesWith(response);

        await handoff.RunCommand.ExecuteAsync(null);

        Assert.Equal(RunState.Completed, handoff.State);
        var request = Assert.Single(fake.HandoffRequests);
        Assert.Equal("invocation/one", request.InvocationId);
        Assert.Empty(request.Selection.TextIds);
        Assert.Empty(request.Selection.Words);
        Assert.Equal(@"C:\out", handoff.OutputDirectory);
        Assert.Equal(
            new[] { "grammar.json", "texts/one.flextext.json" },
            handoff.Files.Select(file => file.RelativePath));
        Assert.Equal(
            new[] { Path.GetFullPath(@"C:\out\grammar.json"), Path.GetFullPath(@"C:\out\texts\one.flextext.json") },
            handoff.Files.Select(file => file.FullPath));
    }

    [Fact]
    public async Task PathsThatEscapeTheOutputDirectoryDoNotBecomeDraggableRows()
    {
        var (fake, _, handoff) = NewViewModel();
        var response = NewResponse(@"C:\out", "grammar.json", @"C:\out-evil\file.txt");
        fake.HandoffCompletesWith(response);

        await handoff.RunCommand.ExecuteAsync(null);

        var row = Assert.Single(handoff.Files);
        Assert.Equal("grammar.json", row.RelativePath);
    }

    // ADR 0045 decision 13: what leaves the machine is stated once beside the tiles, not read from a file.
    [Fact]
    public void DataSensitivitySentenceNamesWhatLeavesTheMachine()
    {
        Assert.Contains("chat model", HandoffViewModel.DataSensitivitySentence, StringComparison.Ordinal);
        Assert.Contains(
            "OpenAI, Anthropic, or another provider", HandoffViewModel.DataSensitivitySentence,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("grammar.json", HandoffFileKind.Json)]
    [InlineData("statistics/word.jsonl", HandoffFileKind.Jsonl)]
    [InlineData("instructions.md", HandoffFileKind.Markdown)]
    [InlineData("read_handoff.py", HandoffFileKind.Python)]
    [InlineData("selection.txt", HandoffFileKind.Text)]
    [InlineData("texts/example.flextext.xml", HandoffFileKind.Xml)]
    [InlineData("reference/README", HandoffFileKind.Unknown)]
    public void HandoffFileKindIsDerivedFromTheRelativePathExtension(
        string relativePath, HandoffFileKind expectedKind)
    {
        Assert.Equal(expectedKind, HandoffFileViewModel.KindFromRelativePath(relativePath));
    }

    // The pasted header and handoff.md are generated per run (ADR 0045 decisions 4 and 5), not a static asset.
    [Fact]
    public async Task RunningPopulatesThePastedHeaderAndHandoffMarkdownFromTheResponse()
    {
        var (fake, _, handoff) = NewViewModel();
        var response = NewResponseWithHeader(@"C:\out", "pasted header text", "# handoff.md content");
        fake.HandoffCompletesWith(response);

        await handoff.RunCommand.ExecuteAsync(null);

        Assert.Equal("pasted header text", handoff.PastedHeader);
        Assert.Equal("# handoff.md content", handoff.HandoffMarkdown);
    }

    [Fact]
    public void ResetClearsThePastedHeaderAndHandoffMarkdown()
    {
        var (_, _, handoff) = NewViewModel();
        handoff.PastedHeader = "stale";
        handoff.HandoffMarkdown = "stale";

        handoff.Reset();

        Assert.Null(handoff.PastedHeader);
        Assert.Null(handoff.HandoffMarkdown);
    }

    [Fact]
    public async Task RunningReplaysTheCommandsProgressStepsInOrderWithoutInventingAnyOfItsOwn()
    {
        var (fake, _, handoff) = NewViewModel();
        var steps = new[]
        {
            new AssessmentProgress(AssessmentStage.ImportingGrammar, 0, null, "Importing the grammar..."),
            new AssessmentProgress(AssessmentStage.ReadingStatistics, 0, null, "Reading PanGloss's statistics..."),
            new AssessmentProgress(AssessmentStage.Complete, 0, null, "Handoff complete."),
        };
        fake.HandoffCompletesWith(NewResponse(@"C:\out"), steps);
        var observed = new List<AssessmentProgress>();
        handoff.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HandoffViewModel.Progress)) observed.Add(handoff.Progress!);
        };

        await handoff.RunCommand.ExecuteAsync(null);

        Assert.Equal(steps, observed);
        Assert.Equal(RunState.Completed, handoff.State);
    }

    [Fact]
    public async Task CancellingWhileRunningReachesCancelledWithTheCommandsOwnRefusalCode()
    {
        var (fake, _, handoff) = NewViewModel();
        var cancelledRefusal = new Refusal(
            "handoff.cancelled", FailureReason.Cancelled, "The Handoff run was cancelled.");
        fake.HandoffBlocksUntilCancelled(cancelledRefusal);

        // Cancelling is transient: the run can finish before Execute returns, so record rather than sample.
        var states = new List<RunState>();
        handoff.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(HandoffViewModel.State)) states.Add(handoff.State);
        };

        var running = handoff.RunCommand.ExecuteAsync(null);
        Assert.Equal(RunState.Running, handoff.State);
        Assert.True(handoff.CancelCommand.CanExecute(null));

        handoff.CancelCommand.Execute(null);
        Assert.False(handoff.CancelCommand.CanExecute(null));

        await running;

        Assert.Equal(
            new[] { RunState.Running, RunState.Cancelling, RunState.Cancelled }, states);
        Assert.Equal(RunState.Cancelled, handoff.State);
        Assert.Equal("handoff.cancelled", handoff.Refusal!.Code);
        Assert.True(handoff.RunCommand.CanExecute(null));
    }

    [Fact]
    public async Task ARefusalThatIsNotCancellationReachesRefusedAndDisplaysTheCommandsOwnMessage()
    {
        var (fake, _, handoff) = NewViewModel();
        var refusal = new Refusal(
            "handoff.destination-exists", FailureReason.InvalidArgument,
            "The destination already exists and is not empty.",
            new Dictionary<string, string> { ["outputDirectory"] = @"C:\out" });
        fake.HandoffRefusesWith(refusal);

        await handoff.RunCommand.ExecuteAsync(null);

        Assert.Equal(RunState.Refused, handoff.State);
        Assert.Same(refusal, handoff.Refusal);
        Assert.Equal([$"outputDirectory: {@"C:\out"}"], handoff.RefusalFacts);
        Assert.True(handoff.RunCommand.CanExecute(null));
    }

    [Fact]
    public async Task DisposalCancelsAndAwaitsAnActiveRun()
    {
        var (fake, _, handoff) = NewViewModel();
        var cancelledRefusal = new Refusal(
            "handoff.cancelled", FailureReason.Cancelled, "The Handoff run was cancelled.");
        fake.HandoffBlocksUntilCancelled(cancelledRefusal);

        _ = handoff.RunCommand.ExecuteAsync(null);
        Assert.Equal(RunState.Running, handoff.State);

        await handoff.DisposeAsync();

        Assert.Equal(RunState.Cancelled, handoff.State);
    }

    [Fact]
    public async Task DragFileAsyncHandsTheDragAdapterExactlyThatFilesPath()
    {
        var (fake, dragSource, handoff) = NewViewModel();
        fake.HandoffCompletesWith(NewResponse(@"C:\out", "grammar.json", "texts/one.flextext.json"));
        await handoff.RunCommand.ExecuteAsync(null);
        var file = handoff.Files.Single(row => row.RelativePath == "texts/one.flextext.json");

        await handoff.DragFileAsync(null!, file);

        Assert.Equal([file.FullPath], dragSource.LastPaths);
    }

    [Fact]
    public async Task DragAllFilesAsyncHandsTheDragAdapterEveryFilesPath()
    {
        var (fake, dragSource, handoff) = NewViewModel();
        fake.HandoffCompletesWith(NewResponse(@"C:\out", "grammar.json", "texts/one.flextext.json"));
        await handoff.RunCommand.ExecuteAsync(null);

        await handoff.DragAllFilesAsync(null!);

        Assert.Equal(handoff.Files.Select(file => file.FullPath), dragSource.LastPaths);
    }

    private sealed class FakeFolderPicker(string? folder) : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(folder);
    }

    private sealed class FakeDragSource : IFileDragSource
    {
        public IReadOnlyList<string>? LastPaths { get; private set; }

        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects)
        {
            LastPaths = filePaths;
            return Task.FromResult(allowedEffects);
        }
    }
}
