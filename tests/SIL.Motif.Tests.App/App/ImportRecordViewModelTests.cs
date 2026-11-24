using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ImportRecordViewModelTests
{
    private const string Project = "project.fwdata";

    private static RecordImportResponse Plan(int page = 1, int pageCount = 1, int operations = 2, int refusals = 0,
        string? draft = null) =>
        new(draft is null ? "dryRun" : "draft", "lexiconEntry", "entry-1", "fresh", ChangeCount: operations,
            OperationCount: operations, RefusalCount: refusals, Page: page, PageCount: pageCount,
            Operations: Enumerable.Range(0, Math.Min(operations, 3))
                .Select(i => new RecordImportOperation($"op-{page}-{i}", "lexical/lexSense/setGloss", "sense-1")).ToArray(),
            Refusals: Enumerable.Range(0, Math.Min(refusals, 3))
                .Select(i => new RecordImportRefusal("record-import.field-uncovered", "LexSense", $"field{i}", null, "Not covered."))
                .ToArray(),
            DraftName: draft, ProposalId: draft is null ? null : "proposal-1");

    private static Task<CommandOutcome<RecordImportResponse>> Ok(RecordImportResponse response) =>
        Task.FromResult(CommandOutcome<RecordImportResponse>.Success(response));

    private static Task<CommandOutcome<RecordImportResponse>> Refused(string code) =>
        Task.FromResult(CommandOutcome<RecordImportResponse>.Refused(new(code, FailureReason.Refused, "Declined.")));

    private static (ImportRecordViewModel Model, FakeCommandClient Client) Make(
        Func<RecordImportRequest, CancellationToken, Task<CommandOutcome<RecordImportResponse>>> handler)
    {
        var client = new FakeCommandClient { StageRecordImportHandler = handler };
        return (new ImportRecordViewModel(client, Project) { FilePath = "record.md" }, client);
    }

    [UiThreadFact]
    public async Task PreviewShowsCountsChangesAndRefusalsWithoutADraftName()
    {
        var (model, client) = Make((_, _) => Ok(Plan(operations: 2, refusals: 1)));

        await model.PreviewAsync();

        Assert.Equal(ImportRecordState.Loaded, model.State);
        Assert.Equal(2, model.OperationCount);
        Assert.Equal(1, model.RefusalCount);
        Assert.Equal(2, model.Operations.Count);
        Assert.Equal("record-import.field-uncovered", Assert.Single(model.Refusals).Code);
        var request = Assert.Single(client.StageRecordImportRequests);
        Assert.Null(request.DraftName);
        Assert.Equal(Project, request.ProjectPath);
        Assert.False(model.IsBusy);
    }

    [UiThreadFact]
    public async Task PagingRequestsTheNeighbouringPageAndStopsAtTheEnds()
    {
        var (model, client) = Make((request, _) => Ok(Plan(request.Page, pageCount: 2, operations: 5)));
        await model.PreviewAsync();
        Assert.False(model.PreviousPageCommand.CanExecute(null));
        Assert.True(model.NextPageCommand.CanExecute(null));

        await model.NextPageCommand.ExecuteAsync(null);

        Assert.Equal(2, model.Page);
        Assert.Equal(2, client.StageRecordImportRequests[^1].Page);
        Assert.False(model.NextPageCommand.CanExecute(null));
        Assert.True(model.PreviousPageCommand.CanExecute(null));
    }

    [UiThreadTheory]
    [InlineData(RefusalCodes.RecordImportPlanRefused)]
    [InlineData(RefusalCodes.RecordImportInvalidPage)]
    [InlineData(RefusalCodes.RecordImportFileUnreadable)]
    public async Task ARefusedPreviewShowsTheRefusalAndCannotBeStaged(string code)
    {
        var (model, _) = Make((_, _) => Refused(code));
        model.DraftName = "Import";

        await model.PreviewAsync();

        Assert.Equal(ImportRecordState.Refused, model.State);
        Assert.Equal(code, model.Refusal!.Code);
        Assert.Null(model.Preview);
        Assert.False(model.CanStage);
    }

    [UiThreadFact]
    public async Task StageNeedsADraftNameThenReturnsTheDraftId()
    {
        var (model, client) = Make((request, _) => Ok(request.DraftName is null ? Plan() : Plan(draft: request.DraftName)));
        await model.PreviewAsync();
        Assert.False(model.CanStage);

        model.DraftName = "  Import one ";
        Assert.True(model.CanStage);
        await model.StageAsync();

        Assert.Equal(ImportRecordState.Staged, model.State);
        Assert.Equal("proposal-1", model.StagedProposalId);
        Assert.Equal("Import one", model.StagedDraftName);
        Assert.Equal("Import one", client.StageRecordImportRequests[^1].DraftName);
        Assert.False(model.CanStage);
    }

    [UiThreadFact]
    public async Task APreviewWithNoOperationsCannotBeStaged()
    {
        var (model, _) = Make((_, _) => Ok(Plan(operations: 0, refusals: 1)));
        model.DraftName = "Import";

        await model.PreviewAsync();

        Assert.Equal(ImportRecordState.Loaded, model.State);
        Assert.False(model.CanStage);
    }

    [UiThreadFact]
    public async Task ARefusedStageKeepsThePreviewSoTheNameCanBeFixed()
    {
        var (model, _) = Make((request, _) =>
            request.DraftName is null ? Ok(Plan()) : Refused(RefusalCodes.DraftNameCollision));
        model.DraftName = "Taken";
        await model.PreviewAsync();

        await model.StageAsync();

        Assert.Equal(ImportRecordState.Refused, model.State);
        Assert.Equal(RefusalCodes.DraftNameCollision, model.Refusal!.Code);
        Assert.NotNull(model.Preview);
        Assert.Null(model.StagedProposalId);
        Assert.True(model.CanStage);
    }

    [UiThreadFact]
    public async Task ChangingTheFileDropsThePreviewAndAReviewReadsTheNewOne()
    {
        var operations = 2;
        var (model, client) = Make((_, _) => Ok(Plan(operations: operations)));
        model.DraftName = "Import";
        await model.PreviewAsync();
        Assert.True(model.CanStage);

        model.FilePath = "edited.md";
        Assert.Equal(ImportRecordState.Idle, model.State);
        Assert.Null(model.Preview);
        Assert.Empty(model.Operations);
        Assert.False(model.CanStage);

        operations = 3;
        await model.PreviewAsync();

        Assert.Equal(3, model.OperationCount);
        Assert.Equal("edited.md", client.StageRecordImportRequests[^1].FilePath);
        Assert.Equal(ImportRecordState.Loaded, model.State);
    }

    [UiThreadFact]
    public async Task APreviewAgainAfterStagingStartsOver()
    {
        var (model, _) = Make((request, _) => Ok(Plan(draft: request.DraftName)));
        model.DraftName = "Import";
        await model.PreviewAsync();
        await model.StageAsync();

        await model.PreviewAsync();

        Assert.Equal(ImportRecordState.Loaded, model.State);
    }

    [UiThreadFact]
    public async Task CommandsAreDisabledWhileBusyAndCancelRestoresThePriorPage()
    {
        var started = new TaskCompletionSource();
        var blocking = false;
        var (model, _) = Make(async (_, token) =>
        {
            if (!blocking) return await Ok(Plan());
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        });
        model.DraftName = "Import";
        await model.PreviewAsync();
        blocking = true;

        var running = model.PreviewAsync();
        await started.Task;

        Assert.True(model.IsBusy);
        Assert.False(model.PreviewCommand.CanExecute(null));
        Assert.False(model.StageCommand.CanExecute(null));
        Assert.True(model.CancelCommand.CanExecute(null));

        model.CancelCommand.Execute(null);
        await running;

        Assert.False(model.IsBusy);
        Assert.Equal(ImportRecordState.Loaded, model.State);
        Assert.NotNull(model.Preview);
        Assert.True(model.PreviewCommand.CanExecute(null));
    }

    [UiThreadFact]
    public async Task ACancelledFirstPreviewStaysIdle()
    {
        var started = new TaskCompletionSource();
        var (model, _) = Make(async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        });

        var running = model.PreviewAsync();
        await started.Task;
        model.Cancel();
        await running;

        Assert.Equal(ImportRecordState.Idle, model.State);
        Assert.Null(model.Refusal);
        Assert.False(model.IsBusy);
    }

    [UiThreadFact]
    public async Task AnUnexpectedFailureIsAnErrorState()
    {
        var (model, _) = Make((_, _) => throw new IOException("disk gone"));

        await model.PreviewAsync();

        Assert.Equal(ImportRecordState.Error, model.State);
        Assert.False(model.IsBusy);
        Assert.False(model.CanStage);
    }

    [Fact]
    public void PreviewNeedsAFile()
    {
        var model = new ImportRecordViewModel(new FakeCommandClient(), Project);

        Assert.False(model.PreviewCommand.CanExecute(null));
        model.FilePath = "record.md";
        Assert.True(model.PreviewCommand.CanExecute(null));
    }

    [UiThreadFact]
    public async Task APreviewInFlightWhenTheFileChangesIsNeverApplied()
    {
        var release = new TaskCompletionSource<CommandOutcome<RecordImportResponse>>();
        var started = new TaskCompletionSource();
        var (model, client) = Make((request, _) =>
        {
            if (request.FilePath != "record.md") return Ok(Plan(operations: 3));
            started.SetResult();
            return release.Task;
        });

        var running = model.PreviewAsync();
        await started.Task;
        model.FilePath = "other.md";
        release.SetResult(CommandOutcome<RecordImportResponse>.Success(Plan(operations: 1)));
        await running;

        Assert.Equal(ImportRecordState.Idle, model.State);
        Assert.Null(model.Preview);
        Assert.False(model.IsBusy);
        await model.PreviewAsync();
        Assert.Equal(3, model.OperationCount);
        Assert.Equal("other.md", client.StageRecordImportRequests[^1].FilePath);
    }

    [UiThreadFact]
    public async Task CancelIsUnavailableWhileStagingAndTheResultStillSurfaces()
    {
        var release = new TaskCompletionSource<CommandOutcome<RecordImportResponse>>();
        var started = new TaskCompletionSource();
        var (model, _) = Make((request, _) =>
        {
            if (request.DraftName is null) return Ok(Plan());
            started.SetResult();
            return release.Task;
        });
        model.DraftName = "Import";
        await model.PreviewAsync();

        var running = model.StageAsync();
        await started.Task;
        Assert.True(model.IsBusy);
        Assert.False(model.CancelCommand.CanExecute(null));
        model.Cancel();
        model.FilePath = "other.md";
        release.SetResult(CommandOutcome<RecordImportResponse>.Success(Plan(draft: "Import")));
        await running;

        Assert.Equal(ImportRecordState.Staged, model.State);
        Assert.Equal("proposal-1", model.StagedProposalId);
        Assert.False(model.IsBusy);
    }

    [UiThreadFact]
    public async Task ARefusedPageNavigationKeepsTheCurrentPreview()
    {
        var (model, _) = Make((request, _) =>
            request.Page == 1 ? Ok(Plan(pageCount: 2, operations: 5)) : Refused(RefusalCodes.RecordImportInvalidPage));
        await model.PreviewAsync();

        await model.NextPageCommand.ExecuteAsync(null);

        Assert.Equal(ImportRecordState.Refused, model.State);
        Assert.NotNull(model.Preview);
        Assert.Equal(1, model.Page);
    }

    [UiThreadFact]
    public async Task DisposeCancelsAPreviewInFlight()
    {
        var started = new TaskCompletionSource();
        CancellationToken seen = default;
        var (model, _) = Make(async (_, token) =>
        {
            seen = token;
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        });

        var running = model.PreviewAsync();
        await started.Task;
        model.Dispose();
        await running;

        Assert.True(seen.IsCancellationRequested);
    }
}
