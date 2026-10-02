using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins what the diagnostic tools, Try a Word, and AI Handoff hand the clipboard and the diagnostic file
/// dialogs, through scripted doubles rather than the window's own clipboard and storage provider.
/// </summary>
public sealed class DesktopFileAndClipboardSeamTests
{
    private const string ValidDiagnosticJson = """
        {"schemaVersion":"pangloss.trace-details.v3","word":"word",
         "search":{"completed":false,"capped":false,"timedOut":false,"invalidShape":false,"steps":1,"elapsedNs":2},
         "result":{"signature":"-","guessed":false,"analyses":[]},"categories":{},"trace":null}
        """;

    private const string InvalidShapeDiagnosticJson = """
        {"schemaVersion":"pangloss.trace-details.v3","word":"word",
         "search":{"completed":false,"capped":false,"timedOut":false,"invalidShape":true,"steps":0,"elapsedNs":0},
         "result":{"signature":"-","guessed":false,"analyses":[]},"categories":{},"trace":null}
        """;

    private const string ExpectedChatInstructions =
        "Interpret this saved Motif diagnostic as recorded evidence. Read analyses before attempts, keep parser order, " +
        "treat incomplete search as incomplete even with a success, do not infer trace completion from a clean exit, " +
        "and label category counts aggregate rather than step timing. Format guide: " +
        "https://github.com/sillsdev/motif/blob/main/docs/handoff/trace-diagnostic-format.md";

    private readonly RecordingClipboard _clipboard = new();
    private readonly ScriptedDiagnosticFiles _files = new();

    private DiagnosticToolsViewModel ToolsFor(string? json = null) =>
        new(json is null ? new TraceWordViewModel() : TraceWordViewModel.FromDiagnosticJson(json), _clipboard, _files,
            _files);

    [Fact]
    public async Task CopyingTheDiagnosticPutsItsExactJsonOnTheClipboard()
    {
        var tools = ToolsFor(ValidDiagnosticJson);

        await tools.CopyJsonAsync();

        Assert.Equal(ValidDiagnosticJson, Assert.Single(_clipboard.Copied));
        Assert.Null(tools.Error);
    }

    [Fact]
    public async Task CopyingForAChatModelIncludesInstructionsSummaryAndTheUnchangedTrace()
    {
        var tools = ToolsFor(ValidDiagnosticJson);

        await tools.CopyInstructionsAsync();

        var copied = Assert.Single(_clipboard.Copied);
        Assert.Contains(ExpectedChatInstructions, copied, StringComparison.Ordinal);
        Assert.Contains("Summary:\nNo parse", copied, StringComparison.Ordinal);
        Assert.Contains("1 parser steps", copied, StringComparison.Ordinal);
        Assert.Contains("overall time not recorded", copied, StringComparison.Ordinal);
        Assert.Contains("Search completion: incomplete", copied, StringComparison.Ordinal);
        Assert.Contains("Trace diagnostic JSON:\n" + ValidDiagnosticJson, copied, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, "complete", "Search complete")]
    [InlineData(false, true, "incomplete", "Search incomplete")]
    public async Task ChatCompletionAgreesWithTheRecordedDiagnosticAndWindow(bool completed, bool capped,
        string completion, string windowStatus)
    {
        var json = ValidDiagnosticJson.Replace("\"completed\":false", "\"completed\":" + completed.ToString().ToLowerInvariant())
            .Replace("\"capped\":false", "\"capped\":" + capped.ToString().ToLowerInvariant());
        var tools = ToolsFor(json);
        await tools.CopyInstructionsAsync();
        Assert.Equal(completed, tools.Trace.Result!.Complete);
        Assert.StartsWith(windowStatus, tools.Trace.SearchStatusText);
        Assert.Contains("Search completion: " + completion, Assert.Single(_clipboard.Copied), StringComparison.Ordinal);
        Assert.Contains(json, _clipboard.Copied[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnavailableSearchCompletionStaysUnavailableInTheWindowAndChat()
    {
        var tools = ToolsFor(ValidDiagnosticJson);
        tools.Trace.Result = tools.Trace.Result! with { Complete = true, SearchStatus = "unavailable" };
        await tools.CopyInstructionsAsync();
        Assert.Equal("Search completion not recorded", tools.Trace.SearchStatusText);
        Assert.Contains("Search completion: not recorded", Assert.Single(_clipboard.Copied));
    }

    [Fact]
    public async Task CopyingAnInvalidShapeSaysTheSearchDidNotRun()
    {
        var tools = ToolsFor(InvalidShapeDiagnosticJson);

        await tools.CopyInstructionsAsync();

        var copied = Assert.Single(_clipboard.Copied);
        Assert.Contains("Search completion: not run (invalid shape recorded)", copied, StringComparison.Ordinal);
        Assert.DoesNotContain("Search completion: incomplete", copied, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AClipboardThatFailsIsShownAsADiagnosticNotWritten()
    {
        var tools = ToolsFor(ValidDiagnosticJson);
        _clipboard.Failure = new InvalidOperationException("clipboard is busy");

        await tools.CopyJsonAsync();

        Assert.Equal(WindowRefusal.DiagnosticNotWrittenCode, tools.Error?.Code);
        Assert.Equal("Motif could not copy the diagnostic.", tools.Error?.Sentence);
        Assert.Contains("clipboard is busy", tools.Error?.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavingWritesTheExactJsonUnderTheWordsSuggestedName()
    {
        var tools = ToolsFor(ValidDiagnosticJson);

        await tools.SaveAsync();

        var saved = Assert.Single(_files.Saved);
        Assert.Equal("word.trace.json", saved.SuggestedFileName);
        Assert.Equal(ValidDiagnosticJson, saved.Json);
        Assert.Null(tools.Error);
    }

    [Fact]
    public async Task ACancelledSaveWritesNothingAndShowsNoError()
    {
        var tools = ToolsFor(ValidDiagnosticJson);
        _files.CancelSaves = true;

        await tools.SaveAsync();

        Assert.Equal(1, _files.SavePrompts);
        Assert.Empty(_files.Saved);
        Assert.Null(tools.Error);
    }

    [Fact]
    public async Task ASaveThatFailsIsShownAsADiagnosticNotWritten()
    {
        var tools = ToolsFor(ValidDiagnosticJson);
        _files.SaveFailure = new IOException("disk full");

        await tools.SaveAsync();

        Assert.Equal(WindowRefusal.DiagnosticNotWrittenCode, tools.Error?.Code);
        Assert.Equal("Motif could not save the diagnostic file.", tools.Error?.Sentence);
    }

    [Fact]
    public async Task ANewActionClearsTheLastActionsError()
    {
        var tools = ToolsFor(ValidDiagnosticJson);
        _files.SaveFailure = new IOException("disk full");
        await tools.SaveAsync();
        Assert.NotNull(tools.Error);

        await tools.CopyJsonAsync();

        Assert.Null(tools.Error);
    }

    [Fact]
    public async Task OpeningAValidFileHandsItOnForAWindowWithItsOwnDialogs()
    {
        var tools = ToolsFor();
        OpenedDiagnostic? opened = null;
        tools.DiagnosticOpened += diagnostic => opened = diagnostic;
        _files.NextOpenReads(ValidDiagnosticJson);

        await tools.OpenAsync();

        Assert.NotNull(opened);
        Assert.Equal(ValidDiagnosticJson, opened!.Trace.DiagnosticJson);
        Assert.Null(opened.Refusal);
        Assert.Null(tools.Error);

        var windowsDialogs = new ScriptedDiagnosticFiles();
        var windowTools = tools.ForOpened(opened, windowsDialogs);
        await windowTools.SaveAsync();
        await windowTools.CopyJsonAsync();
        Assert.Equal(("word.trace.json", ValidDiagnosticJson), Assert.Single(windowsDialogs.Saved));
        Assert.Empty(_files.Saved);
        Assert.Equal(ValidDiagnosticJson, Assert.Single(_clipboard.Copied));
    }

    [Fact]
    public async Task ACancelledReadIsACancelledOpenNotAnUnreadableFile()
    {
        var tools = ToolsFor();
        var openedCount = 0;
        tools.DiagnosticOpened += _ => openedCount++;
        _files.NextOpenFails(new OperationCanceledException());

        await tools.OpenAsync();

        Assert.Equal(1, _files.OpenPrompts);
        Assert.Equal(0, openedCount);
        Assert.Null(tools.Error);
    }

    [Fact]
    public async Task TheOpenerTreatsACancelledReadAsACancelledOpen()
    {
        var shown = 0;
        var refused = 0;

        await SavedDiagnosticOpener.OpenAsync(
            () => Task.FromCanceled<string?>(new CancellationToken(canceled: true)), _ => shown++, _ => refused++);

        Assert.Equal(0, shown);
        Assert.Equal(0, refused);
    }

    [Fact]
    public async Task ACancelledOpenShowsNothing()
    {
        var tools = ToolsFor();
        var openedCount = 0;
        tools.DiagnosticOpened += _ => openedCount++;
        _files.NextOpenCancels();

        await tools.OpenAsync();

        Assert.Equal(1, _files.OpenPrompts);
        Assert.Equal(0, openedCount);
        Assert.Null(tools.Error);
    }

    [Fact]
    public async Task AMalformedFileIsRefusedWhereItWasOpened()
    {
        var tools = ToolsFor();
        var openedCount = 0;
        tools.DiagnosticOpened += _ => openedCount++;
        _files.NextOpenReads("{");

        await tools.OpenAsync();

        Assert.Equal(0, openedCount);
        Assert.Equal(RefusalCodes.WordTraceMalformedDiagnostic, tools.Error?.Code);
    }

    [Fact]
    public async Task AFileThatCannotBeReadIsRefusedWhereItWasOpened()
    {
        var tools = ToolsFor();
        _files.NextOpenFails(new IOException("locked"));

        await tools.OpenAsync();

        Assert.Equal(WindowRefusal.DiagnosticUnreadableCode, tools.Error?.Code);
        Assert.Equal("Motif could not read that diagnostic file.", tools.Error?.Sentence);
    }

    [Fact]
    public async Task TryAWordOpensAValidSavedDiagnosticForAWindowOfItsOwn()
    {
        var page = new TryWordPageModel(NewContext());
        OpenedDiagnostic? opened = null;
        page.SavedDiagnosticOpened += diagnostic => opened = diagnostic;
        _files.NextOpenReads(ValidDiagnosticJson);

        await page.OpenSavedDiagnosticAsync();

        Assert.Equal(ValidDiagnosticJson, opened?.Trace.DiagnosticJson);
        Assert.Null(opened?.Refusal);
        Assert.Null(page.Diagnostics.Error);
        Assert.False(page.Trace.HasResult);
    }

    [Fact]
    public async Task TryAWordShowsARefusedSavedDiagnosticInAWindowOfItsOwn()
    {
        var page = new TryWordPageModel(NewContext());
        OpenedDiagnostic? opened = null;
        page.SavedDiagnosticOpened += diagnostic => opened = diagnostic;
        _files.NextOpenReads("{");

        await page.OpenSavedDiagnosticAsync();

        Assert.NotNull(opened);
        Assert.Equal(RefusalCodes.WordTraceMalformedDiagnostic, opened!.Refusal?.Code);
        Assert.False(opened.Trace.HasResult);
        Assert.Null(page.Diagnostics.Error);
        Assert.Equal(opened.Refusal, page.Diagnostics.ForOpened(opened, _files).Error);
    }

    [Fact]
    public async Task TryAWordIgnoresACancelledOpen()
    {
        var page = new TryWordPageModel(NewContext());
        var openedCount = 0;
        page.SavedDiagnosticOpened += _ => openedCount++;
        _files.NextOpenCancels();

        await page.OpenSavedDiagnosticAsync();

        Assert.Equal(1, _files.OpenPrompts);
        Assert.Equal(0, openedCount);
    }

    [Fact]
    public async Task TryAWordsDiagnosticToolsCopyTheSharedTrace()
    {
        var context = NewContext();
        var page = new TryWordPageModel(context);

        Assert.Same(page.Trace, page.Diagnostics.Trace);
        await page.Diagnostics.CopyInstructionsAsync();
        var copied = Assert.Single(_clipboard.Copied);
        Assert.Contains(ExpectedChatInstructions, copied, StringComparison.Ordinal);
        Assert.Contains("Summary:\nNot recorded.", copied, StringComparison.Ordinal);
        Assert.Contains("Trace diagnostic JSON:\nNot recorded.", copied, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAiHandoffPageCopiesThroughTheComposedClipboard()
    {
        var page = new AiHandoffPageModel(NewContext());
        page.Handoff.PastedHeader = "the starter prompt";

        await page.Handoff.CopyStarterPromptAsync();

        Assert.Equal("the starter prompt", Assert.Single(_clipboard.Copied));
    }

    [Fact]
    public async Task HandoffCopiesTheExactFolderPathQuestionAndStarterPrompt()
    {
        var handoff = NewHandoff();
        handoff.OutputDirectory = @"C:\handoff";
        var file = new HandoffFileViewModel("grammar.json", @"C:\handoff\grammar.json");
        handoff.Files.Add(file);
        handoff.PastedHeader = "Read handoff.md first.";

        await handoff.CopyFolderAsync();
        await handoff.CopyFilePathAsync(file);
        await handoff.CopyQuestionAsync("Which words did not parse?");
        await handoff.CopyStarterPromptAsync();

        Assert.Equal(
            [@"C:\handoff", @"C:\handoff\grammar.json", "Which words did not parse?", "Read handoff.md first."],
            _clipboard.Copied);
    }

    [Fact]
    public async Task HandoffCopiesNothingBeforeTheFilesAreWritten()
    {
        var handoff = NewHandoff();

        await handoff.CopyFolderAsync();
        await handoff.CopyStarterPromptAsync();

        Assert.Empty(_clipboard.Copied);
    }

    private HandoffViewModel NewHandoff()
    {
        var fake = new FakeCommandClient();
        return new HandoffViewModel(fake, new SelectionViewModel(fake), new NoFolderPicker(), new NoDragSource(),
            clipboard: _clipboard);
    }

    private WorkspaceContext NewContext()
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        var selection = new SelectionViewModel(fake);
        return new WorkspaceContext(selection, new AssessViewModel(fake, selection), new ChangesViewModel(fake), fake,
            new NoFolderPicker(), new NoDragSource(), new BaselineViewModel(fake), clipboard: _clipboard,
            diagnosticFiles: _files, diagnosticDialogs: _files);
    }

    private sealed class NoFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoDragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
