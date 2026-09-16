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

    [Fact]
    public async Task BackingOutOfTheFolderDialogLeavesStateIdleAndNeverCallsHandoff()
    {
        var (fake, _, handoff) = NewViewModel(folder: null);

        await handoff.RunCommand.ExecuteAsync(null);

        Assert.Empty(fake.HandoffRequests);
        Assert.Equal(HandoffRunState.Idle, handoff.State);
        Assert.Null(handoff.OutputDirectory);
        Assert.Empty(handoff.Files);
    }

    [Fact]
    public async Task RunningWritesTheDestinationAndExposesEveryFileAsADraggableRow()
    {
        var (fake, _, handoff) = NewViewModel();
        var response = NewResponse(@"C:\out", "grammar.json", "texts/one.flextext.json");
        fake.HandoffCompletesWith(response);

        await handoff.RunCommand.ExecuteAsync(null);

        Assert.Equal(HandoffRunState.Completed, handoff.State);
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

    // Reading the shipped asset is the point: restating the literal here would pin nothing.
    [Fact]
    public void DataSensitivitySentenceIsTakenVerbatimFromTheInstructionsAsset()
    {
        var instructions = HandoffViewModel.InstructionsMarkdown
            .Replace("\r\n", "\n")
            .Replace("\n", " ")
            .Replace("**", "");

        Assert.Contains(HandoffViewModel.DataSensitivitySentence, instructions, StringComparison.Ordinal);
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

    [Fact]
    public void HandoffIntroductionNamesTheUploadStepsAndReferenceLocations()
    {
        var introduction = HandoffViewModel.IntroductionText;

        Assert.Contains("instructions.md", introduction, StringComparison.Ordinal);
        Assert.Contains("Claude", introduction, StringComparison.Ordinal);
        Assert.Contains("ChatGPT", introduction, StringComparison.Ordinal);
        Assert.Contains("Gemini", introduction, StringComparison.Ordinal);
        Assert.Contains("docs/handoff/grammar-format.md", introduction, StringComparison.Ordinal);
        Assert.Contains("docs/handoff/flextext-json-format.md", introduction, StringComparison.Ordinal);
        Assert.Contains("docs/handoff/hc-mechanics.md", introduction, StringComparison.Ordinal);
        Assert.Contains("src/SIL.Motif.Commands/Handoff/Assets/read_handoff.py", introduction,
            StringComparison.Ordinal);
        Assert.Contains(HandoffViewModel.RepositoryUrlBase, introduction, StringComparison.Ordinal);
    }

    [Fact]
    public void StarterPromptIsEmbeddedAndNamesTheHandoffContents()
    {
        var prompt = HandoffViewModel.StarterPromptMarkdown;

        Assert.Contains("Read `instructions.md` first", prompt, StringComparison.Ordinal);
        Assert.Contains("FieldWorks' last save", prompt, StringComparison.Ordinal);
        Assert.Contains("`grammar.json`", prompt, StringComparison.Ordinal);
        Assert.Contains("`selection.txt`", prompt, StringComparison.Ordinal);
        Assert.Contains("`statistics/*.jsonl`", prompt, StringComparison.Ordinal);
        Assert.Contains("`texts/*.flextext.json`", prompt, StringComparison.Ordinal);
        Assert.Contains("`reference/*.md`", prompt, StringComparison.Ordinal);
        Assert.Contains("`recipes.md`", prompt, StringComparison.Ordinal);
        Assert.Contains("`read_handoff.py`", prompt, StringComparison.Ordinal);
        Assert.Contains("file and record", prompt, StringComparison.Ordinal);
        Assert.Contains("flag guesses", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteFlexTextXmlIsForwardedToTheHandoffRequest()
    {
        var (fake, _, handoff) = NewViewModel();
        fake.HandoffCompletesWith(NewResponse(@"C:\out"));
        handoff.WriteFlexTextXml = true;

        await handoff.RunCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(fake.HandoffRequests).WriteFlexTextXml);
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
        Assert.Equal(HandoffRunState.Completed, handoff.State);
    }

    [Fact]
    public async Task CancellingWhileRunningReachesCancelledWithTheCommandsOwnRefusalCode()
    {
        var (fake, _, handoff) = NewViewModel();
        var cancelledRefusal = new Refusal(
            "handoff.cancelled", FailureReason.Refused, "The Handoff run was cancelled.");
        fake.HandoffBlocksUntilCancelled(cancelledRefusal);

        // Cancelling is transient: the run can finish before Execute returns, so record rather than sample.
        var states = new List<HandoffRunState>();
        handoff.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(HandoffViewModel.State)) states.Add(handoff.State);
        };

        var running = handoff.RunCommand.ExecuteAsync(null);
        Assert.Equal(HandoffRunState.Running, handoff.State);
        Assert.True(handoff.CancelCommand.CanExecute(null));

        handoff.CancelCommand.Execute(null);
        Assert.False(handoff.CancelCommand.CanExecute(null));

        await running;

        Assert.Equal(
            new[] { HandoffRunState.Running, HandoffRunState.Cancelling, HandoffRunState.Cancelled }, states);
        Assert.Equal(HandoffRunState.Cancelled, handoff.State);
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

        Assert.Equal(HandoffRunState.Refused, handoff.State);
        Assert.Same(refusal, handoff.Refusal);
        Assert.Equal([$"outputDirectory: {@"C:\out"}"], handoff.RefusalFacts);
        Assert.True(handoff.RunCommand.CanExecute(null));
    }

    [Fact]
    public async Task DisposalCancelsAndAwaitsAnActiveRun()
    {
        var (fake, _, handoff) = NewViewModel();
        var cancelledRefusal = new Refusal(
            "handoff.cancelled", FailureReason.Refused, "The Handoff run was cancelled.");
        fake.HandoffBlocksUntilCancelled(cancelledRefusal);

        _ = handoff.RunCommand.ExecuteAsync(null);
        Assert.Equal(HandoffRunState.Running, handoff.State);

        await handoff.DisposeAsync();

        Assert.Equal(HandoffRunState.Cancelled, handoff.State);
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
