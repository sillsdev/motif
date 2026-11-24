using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Answers the record file chooser from a script and counts how often it was asked.</summary>
public sealed class ScriptedRecordFiles : IRecordFilePicker
{
    private readonly Queue<string?> _answers = new();

    public int Prompts { get; private set; }

    public void NextChooses(string? path) => _answers.Enqueue(path);

    public Task<string?> PickRecordFileAsync(CancellationToken cancellationToken = default)
    {
        Prompts++;
        return Task.FromResult(_answers.Count > 0 ? _answers.Dequeue() : null);
    }
}

public sealed class ImportRecordDialogModelTests
{
    internal static RecordImportResponse Plan(int operations = 2, int refusals = 1, string? draft = null,
        int page = 1, int pageCount = 1) =>
        new(draft is null ? "dryRun" : "draft", "lexiconEntry", "entry-1", "fresh", operations, operations, refusals,
            page, pageCount,
            Enumerable.Range(0, Math.Min(operations, 3))
                .Select(i => new RecordImportOperation($"op-{i}", "lexical/lexSense/setGloss", $"sense-{i}")).ToArray(),
            Enumerable.Range(0, Math.Min(refusals, 3))
                .Select(i => new RecordImportRefusal("record-import.field-uncovered", "LexSense", $"Gloss{i}", null,
                    "No operation writes this field.")).ToArray(),
            draft, draft is null ? null : "proposal-7");

    internal static Task<CommandOutcome<RecordImportResponse>> Ok(RecordImportResponse response) =>
        Task.FromResult(CommandOutcome<RecordImportResponse>.Success(response));

    internal static Task<CommandOutcome<RecordImportResponse>> Refused(string code) =>
        Task.FromResult(CommandOutcome<RecordImportResponse>.Refused(new(code, FailureReason.Refused, "Declined.")));

    internal static (ImportRecordDialogModel Model, FakeCommandClient Client, ScriptedRecordFiles Files,
        List<string> Opened) Make(
        Func<RecordImportRequest, CancellationToken, Task<CommandOutcome<RecordImportResponse>>>? handler = null,
        Func<string, CancellationToken, Task>? open = null)
    {
        var client = new FakeCommandClient
        {
            StageRecordImportHandler = handler ?? ((request, _) => Ok(Plan(draft: request.DraftName)))
        };
        var files = new ScriptedRecordFiles();
        var opened = new List<string>();
        var model = new ImportRecordDialogModel(new ImportRecordViewModel(client, "project.fwdata"), files,
            open ?? ((id, _) => { opened.Add(id); return Task.CompletedTask; }));
        return (model, client, files, opened);
    }

    [UiThreadFact]
    public async Task BrowseChoosesTheFileAndPreviewsItAtOnce()
    {
        var (model, client, files, _) = Make();
        files.NextChooses("edited.md");

        await model.BrowseCommand.ExecuteAsync(null);

        Assert.Equal("edited.md", model.Import.FilePath);
        Assert.Equal("edited.md", Assert.Single(client.StageRecordImportRequests).FilePath);
        Assert.True(model.HasPreview);
        Assert.Equal(2, model.OperationRows.Count);
        var refusal = Assert.Single(model.RefusalRows);
        Assert.Equal("record-import.field-uncovered", refusal.Code);
        Assert.Equal("LexSense Gloss0", refusal.Where);
        Assert.Equal("No operation writes this field.", refusal.Reason);
        Assert.Contains("2 changes become 2 operations. 1 cannot be imported.", model.StatusText);
    }

    [UiThreadFact]
    public async Task CancellingTheChooserChangesNothing()
    {
        var (model, client, files, _) = Make();
        files.NextChooses(null);

        await model.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(1, files.Prompts);
        Assert.Empty(client.StageRecordImportRequests);
        Assert.False(model.HasPreview);
        Assert.True(model.BrowseCommand.CanExecute(null));
    }

    [Fact]
    public void NothingCanBePreviewedOrStagedWithoutAFileAndAName()
    {
        var (model, _, _, _) = Make();

        Assert.False(model.Import.PreviewCommand.CanExecute(null));
        Assert.False(model.Import.StageCommand.CanExecute(null));
        Assert.False(model.OpenDraftCommand.CanExecute(null));
        Assert.True(model.BrowseCommand.CanExecute(null));
    }

    [UiThreadFact]
    public async Task AWholeFileRefusalIsShownInTheWindowsWords()
    {
        var (model, _, _, _) = Make((_, _) => Refused(RefusalCodes.RecordImportPlanRefused));
        model.Import.FilePath = "old.md";

        await model.Import.PreviewAsync();

        Assert.True(model.HasShownRefusal);
        Assert.Equal(RefusalCodes.RecordImportPlanRefused, model.ShownRefusal!.Code);
        Assert.Contains("older state of this record", model.ShownRefusal.Sentence);
        Assert.False(model.HasPreview);
    }

    [UiThreadFact]
    public async Task StagingOpensTheStagedDraftByItsProposalId()
    {
        var (model, _, _, opened) = Make();
        model.Import.FilePath = "edited.md";
        model.Import.DraftName = "From file";
        await model.Import.PreviewAsync();
        var raised = 0;
        model.DraftOpened += (_, _) => raised++;

        await model.Import.StageAsync();
        await model.OpenTask;

        Assert.Equal("proposal-7", Assert.Single(opened));
        Assert.Equal(1, raised);
        Assert.True(model.IsStaged);
        Assert.Equal("Close", model.CancelText);
        Assert.Contains("From file", model.StatusText);
    }

    [UiThreadFact]
    public async Task ADraftThatCannotBeOpenedStaysOpenableByHand()
    {
        var attempts = 0;
        var (model, _, _, _) = Make(open: (_, _) =>
            ++attempts == 1 ? Task.FromException(new InvalidOperationException("nope")) : Task.CompletedTask);
        model.Import.FilePath = "edited.md";
        model.Import.DraftName = "From file";
        await model.Import.PreviewAsync();

        await model.Import.StageAsync();
        await model.OpenTask;

        Assert.Equal(1, attempts);
        Assert.Equal("nope", model.StatusText);
        Assert.True(model.OpenDraftCommand.CanExecute(null));

        await model.OpenDraftCommand.ExecuteAsync(null);

        Assert.Equal(2, attempts);
        Assert.NotEqual("nope", model.StatusText);
    }

    [UiThreadFact]
    public async Task AnUnexpectedOpenFailureGetsTheGenericSentence()
    {
        var (model, _, _, _) = Make(open: (_, _) => Task.FromException(new IOException("disk")));
        model.Import.FilePath = "edited.md";
        model.Import.DraftName = "From file";
        await model.Import.PreviewAsync();

        await model.Import.StageAsync();
        await model.OpenTask;

        Assert.Equal(MotifAppStrings.ImportRecordOpenFailed, model.StatusText);
    }

    [UiThreadFact]
    public async Task ClosingMidStageLetsTheStageFinishAndStillOpensItsDraft()
    {
        var gate = new TaskCompletionSource<CommandOutcome<RecordImportResponse>>();
        var stageToken = CancellationToken.None;
        var (model, _, _, opened) = Make((request, token) =>
        {
            if (request.DraftName is null) return Ok(Plan());
            stageToken = token;
            return gate.Task;
        });
        model.Import.FilePath = "edited.md";
        model.Import.DraftName = "From file";
        await model.Import.PreviewAsync();
        var staging = model.Import.StageAsync();
        Assert.True(model.Import.IsBusy);

        model.Dispose();
        Assert.False(stageToken.IsCancellationRequested);
        gate.SetResult(CommandOutcome<RecordImportResponse>.Success(Plan(draft: "From file")));
        await staging;
        await model.OpenTask;

        Assert.Equal(ImportRecordState.Staged, model.Import.State);
        Assert.Equal("proposal-7", Assert.Single(opened));
    }

    [UiThreadFact]
    public async Task ClosingMidPreviewCancelsIt()
    {
        var previewToken = CancellationToken.None;
        var (model, _, _, _) = Make((_, token) =>
        {
            previewToken = token;
            return new TaskCompletionSource<CommandOutcome<RecordImportResponse>>().Task;
        });
        model.Import.FilePath = "edited.md";

        _ = model.Import.PreviewAsync();
        model.Dispose();

        Assert.True(previewToken.IsCancellationRequested);
    }

    [Fact]
    public void TypingAPathMakesPreviewAvailableAndTellsItsButton()
    {
        var (model, _, _, _) = Make();
        var changed = 0;
        model.Import.PreviewCommand.CanExecuteChanged += (_, _) => changed++;

        model.Import.FilePath = "typed.md";

        Assert.True(changed > 0);
        Assert.True(model.Import.PreviewCommand.CanExecute(null));
    }

    [UiThreadFact]
    public async Task BrowseIsOffWhileAPreviewRunsAndCancelStopsIt()
    {
        var gate = new TaskCompletionSource<CommandOutcome<RecordImportResponse>>();
        var (model, _, _, _) = Make((_, token) =>
        {
            token.Register(() => gate.TrySetCanceled(token));
            return gate.Task;
        });
        model.Import.FilePath = "edited.md";

        var running = model.Import.PreviewAsync();

        Assert.True(model.Import.IsBusy);
        Assert.False(model.BrowseCommand.CanExecute(null));
        Assert.False(model.Import.PreviewCommand.CanExecute(null));
        Assert.Equal(MotifAppStrings.ImportRecordReading, model.StatusText);

        model.Cancel();
        await running;

        Assert.False(model.Import.IsBusy);
        Assert.True(model.BrowseCommand.CanExecute(null));
    }

    [UiThreadFact]
    public async Task PagingMovesThroughTheListsAndShowsThePage()
    {
        var (model, _, _, _) = Make((request, _) => Ok(Plan(operations: 5, page: request.Page, pageCount: 2)));
        model.Import.FilePath = "edited.md";
        await model.Import.PreviewAsync();

        Assert.True(model.HasPaging);
        Assert.Equal("Page 1 of 2", model.PageText);
        await model.Import.NextPageCommand.ExecuteAsync(null);
        Assert.Equal("Page 2 of 2", model.PageText);
    }
}
