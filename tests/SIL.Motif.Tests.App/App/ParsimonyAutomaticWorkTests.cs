using System.Text.Json;
using Avalonia.Input;
using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ParsimonyAutomaticWorkTests
{
    private const string ProjectA = "/tmp/automatic-parsimony-a.fwdata";
    private const string ProjectB = "/tmp/automatic-parsimony-b.fwdata";
    private static readonly BaselineToken First = Token('1');
    private static readonly BaselineToken Second = Token('2');
    private const string Measure = "P-adhoc-duplicate";

    [Fact]
    public async Task RepeatedBaselineAndPageSignalsJoinOneOwnedJob()
    {
        var fake = Client();
        var entered = Signal();
        fake.EnqueueParsimonyHandler = (_, _) => Task.FromResult(Queued("job/one"));
        fake.OnWaitForJob(async (_, _, token) =>
        {
            if (!token.CanBeCanceled) return Completed("job/one");
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { return Cancelled(); }
            return Completed("job/one");
        });
        var (context, page) = Build(fake, () => First);
        try
        {
            await context.OpenProjectAsync(ProjectA);
            await entered.Task;
            context.OpenPage(WorkspacePage.Parsimony);
            context.OpenPage(WorkspacePage.Overview);
            await page.BaselineCapturedAsync(CancellationToken.None);
            var reload = page.ReloadAsync();
            Assert.False(reload.IsCompleted);
            var request = Assert.Single(fake.EnqueueParsimonyRequests);
            Assert.Equal(ParsimonyAdmission.AllMeasures, request.MeasureId);
            Assert.Equal(ParsimonyEvidenceScopeKind.ProjectApproved, request.EvidenceScope);
            Assert.Null(request.AssessmentIds);
            Assert.Equal(Timeout.InfiniteTimeSpan, Assert.Single(fake.WaitForJobRequests).Timeout);
            await context.StopPageWorkAsync();
            await reload;
            Assert.Empty(fake.AssessRequests);
        }
        finally { await context.StopPageWorkAsync(); }
    }

    [Fact]
    public async Task StopDuringEnqueueCancelsTheReturnedJobAndWaitsForItsDrain()
    {
        var fake = Client();
        var enqueuing = Signal();
        var enqueue = new TaskCompletionSource<CommandOutcome<JobEnqueuedResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.EnqueueParsimonyHandler = (_, _) => { enqueuing.TrySetResult(); return enqueue.Task; };
        var waiting = Signal();
        var drain = Signal();
        var draining = Signal();
        var cancelledAtWait = false;
        string? waitedJob = null;
        fake.OnWaitForJob(async (request, _, token) =>
        {
            if (token.IsCancellationRequested)
            {
                waitedJob = request.JobId;
                cancelledAtWait = true;
                waiting.TrySetResult();
                return Cancelled();
            }
            draining.TrySetResult();
            await drain.Task;
            return Completed(request.JobId);
        });
        var (context, page) = Build(fake, () => First);
        try
        {
            await context.OpenProjectAsync(ProjectA);
            await enqueuing.Task;
            var stopping = context.StopPageWorkAsync();
            Assert.False(stopping.IsCompleted);
            enqueue.SetResult(Queued("job/enqueue-race"));
            await waiting.Task;
            Assert.Equal("job/enqueue-race", waitedJob);
            Assert.True(cancelledAtWait);
            await draining.Task;
            Assert.False(stopping.IsCompleted);
            drain.SetResult();
            await stopping;
            Assert.True(page.AutomaticWork.IsCompletedSuccessfully);
            Assert.NotEqual(ParsimonyPageState.Refused, page.State);
            Assert.Empty(fake.AssessRequests);
        }
        finally
        {
            enqueue.TrySetResult(Queued("job/enqueue-race"));
            drain.TrySetResult();
            await context.StopPageWorkAsync();
        }
    }

    [Fact]
    public async Task AProjectSwitchDrainsTheOldJobAndDiscardsItsLateSuccess()
    {
        var fake = Client();
        var oldWait = Signal();
        var finishOld = Signal();
        var secondSet = Set("second", Report(Second, "report/second", "second"));
        fake.ParsimonySetHandler = (request, _) => Task.FromResult(CommandOutcome<ParsimonyReportSetResponse?>.Success(
            request.FwDataPath == ProjectB ? secondSet : null));
        fake.EnqueueParsimonyHandler = (_, _) => Task.FromResult(Queued("job/old"));
        fake.OnWaitForJob(async (_, _, _) =>
        {
            oldWait.TrySetResult();
            await finishOld.Task;
            return Completed("job/old");
        });
        var tokenForProject = First;
        var (context, page) = Build(fake, () => tokenForProject);
        try
        {
            await context.OpenProjectAsync(ProjectA);
            await oldWait.Task;
            tokenForProject = Second;
            fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Second, null, false));
            var switching = context.OpenProjectAsync(ProjectB);
            Assert.False(switching.IsCompleted);
            finishOld.SetResult();
            await switching;
            Assert.Equal(ProjectB, context.ProjectPath);
            var row = Assert.Single(page.Rows, row => !row.IsHeader);
            Assert.Equal("second", row.Link!.FindingId);
            Assert.Equal(ProjectB, row.Link.ProjectPath);
            Assert.Single(fake.EnqueueParsimonyRequests);
            Assert.Empty(fake.AssessRequests);
        }
        finally { finishOld.TrySetResult(); await context.StopPageWorkAsync(); }
    }

    [Fact]
    public async Task AReplacementBaselineRejectsTheOldCompletionAndStartsOneCurrentJob()
    {
        var fake = Client();
        var token = First;
        var firstWait = Signal();
        var finishFirst = Signal();
        var secondWait = Signal();
        var finishSecond = Signal();
        var currentReady = false;
        fake.ParsimonySetHandler = (_, _) => Task.FromResult(CommandOutcome<ParsimonyReportSetResponse?>.Success(
            currentReady ? Set("replacement", Report(Second, "report/current", "current")) : null));
        fake.EnqueueParsimonyHandler = (_, _) => Task.FromResult(Queued(
            fake.EnqueueParsimonyRequests.Count == 1 ? "job/first" : "job/second"));
        fake.OnWaitForJob(async (request, _, _) =>
        {
            if (request.JobId == "job/first") { firstWait.TrySetResult(); await finishFirst.Task; }
            else { secondWait.TrySetResult(); await finishSecond.Task; currentReady = true; }
            return Completed(request.JobId);
        });
        var (context, page) = Build(fake, () => token);
        try
        {
            await context.OpenProjectAsync(ProjectA);
            await firstWait.Task;
            token = Second;
            context.Baseline = Baseline(Second);
            fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Second, null, false));
            var replacing = page.BaselineCapturedAsync(CancellationToken.None);
            Assert.False(replacing.IsCompleted);
            finishFirst.SetResult();
            await replacing;
            await secondWait.Task;
            Assert.Equal(2, fake.EnqueueParsimonyRequests.Count);
            finishSecond.SetResult();
            await page.AutomaticWork;
            Assert.Equal("current", Assert.Single(page.Rows, row => !row.IsHeader).Link!.FindingId);
            Assert.Empty(fake.AssessRequests);
        }
        finally
        {
            finishFirst.TrySetResult(); finishSecond.TrySetResult();
            await context.StopPageWorkAsync();
        }
    }

    [Fact]
    public async Task AggregatedRowsKeepTheirRealReportForSelectionAndDisposition()
    {
        var fake = Client();
        var otherMeasure = MeasureCatalog.All.First(measure => measure.Id != Measure).Id;
        var first = Report(First, "report/first", "first");
        var second = Report(First, "report/second", "second", otherMeasure);
        fake.ParsimonySetHandler = (_, _) => Task.FromResult(CommandOutcome<ParsimonyReportSetResponse?>.Success(
            Set("current", first, second)));
        fake.ParsimonyViewHandler = (request, _) => View(request, new[] { first, second });
        fake.RecordDispositionHandler = (request, _) => Task.FromResult(CommandOutcome<ComposedOperationsResponse>.Success(
            new ComposedOperationsResponse(request.DraftName, "RecordParsimonyDisposition", [], 1)));
        var (context, page) = Build(fake, () => First);
        try
        {
            await context.OpenProjectAsync(ProjectA);
            var rows = page.Rows.Where(row => !row.IsHeader).ToArray();
            Assert.Equal(2, rows.Length);
            Assert.Equal("2 findings to review", page.AxisSummary);
            var secondRow = Assert.Single(rows, row => row.Link!.FindingId == "second");
            Assert.Equal(second.ReportId, secondRow.Link!.ReportId);
            Assert.False(page.SelectFinding(secondRow.Link with { ReportId = first.ReportId }));
            Assert.True(page.SelectFinding(secondRow.Link));
            await page.KeepCommand.ExecuteAsync(null);
            var staged = Assert.Single(fake.RecordDispositionRequests);
            Assert.Equal(second.ReportId, staged.ReportId);
            Assert.Equal("second", staged.FindingId);
            Assert.Empty(fake.EnqueueParsimonyRequests);
            Assert.Empty(fake.AssessRequests);
        }
        finally { await context.StopPageWorkAsync(); }
    }

    [Fact]
    public async Task LaterPagesKeepTheirReportIdentityAndTheOtherReportsSelection()
    {
        var fake = Client();
        var first = Report(First, "report/paged", "first");
        var late = first.Findings[0] with { FindingId = "late" };
        var second = Report(First, "report/other", "other", MeasureCatalog.All.First(item => item.Id != Measure).Id);
        var reports = new[] { first, second };
        var gate = Signal();
        fake.ParsimonySetHandler = (_, _) => Task.FromResult(CommandOutcome<ParsimonyReportSetResponse?>.Success(
            Set("paged", reports)));
        fake.ParsimonyViewHandler = async (request, cancellation) =>
        {
            if (request.Query.View != ParsimonyViews.ParsimonyActiveFindings || request.Query.Filters.ReportId != first.ReportId)
                return await View(request, reports);
            var isFirstPage = request.Query.Cursor is null;
            if (!isFirstPage) await gate.Task.WaitAsync(cancellation);
            var finding = isFirstPage ? first.Findings[0] : late;
            ParsimonyViewRow[] rows = [new ParsimonyFindingDispositionViewRow(first.ReportId, "bundle", "active",
                finding, null, null, null, null, null, null)];
            return CommandOutcome<ParsimonyNamedViewResponse>.Success(new ParsimonyNamedViewResponse("bundle",
                request.Query.View, 1, ParsimonyMeasureStatus.Computed, [], 2, 1,
                isFirstPage ? "later" : null, isFirstPage, rows, null, null));
        };
        var (context, page) = Build(fake, () => First);
        try
        {
            await context.OpenProjectAsync(ProjectA);
            var before = page.Rows.Where(row => !row.IsHeader).ToArray();
            Assert.Equal(2, before.Length);
            Assert.False(page.RemainingLoad.IsCompleted);
            var selected = Assert.Single(before, row => row.Link!.FindingId == "other");
            Assert.True(page.SelectFinding(selected.Link!));
            gate.SetResult();
            await page.RemainingLoad;
            Assert.Equal(3, page.Rows.Count(row => !row.IsHeader));
            Assert.Equal(first.ReportId, Assert.Single(page.Rows, row => row.Link?.FindingId == "late").Link!.ReportId);
            Assert.All(before, row => Assert.Contains(page.Rows, current => ReferenceEquals(current, row)));
            Assert.Same(selected, Assert.Single(page.Rows, row => row.IsSelected));
            Assert.Empty(fake.EnqueueParsimonyRequests);
        }
        finally { gate.TrySetResult(); await context.StopPageWorkAsync(); }
    }

    [Fact]
    public async Task FirstCaptureStartsOnlyAfterTheBaselineExists()
    {
        var fake = Client();
        BaselineToken? token = null;
        var entered = Signal();
        fake.EnqueueParsimonyHandler = (_, _) => Task.FromResult(Queued("job/captured"));
        fake.OnWaitForJob(async (_, _, cancellation) =>
        {
            if (!cancellation.CanBeCanceled) return Completed("job/captured");
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellation); }
            catch (OperationCanceledException) { return Cancelled(); }
            return Completed("job/captured");
        });
        var (context, page) = Build(fake, () => token);
        try
        {
            await context.OpenProjectAsync(ProjectA);
            Assert.Empty(fake.EnqueueParsimonyRequests);
            token = First;
            context.Baseline = Baseline(First);
            await page.BaselineCapturedAsync(CancellationToken.None);
            await entered.Task;
            await page.BaselineCapturedAsync(CancellationToken.None);
            Assert.Single(fake.EnqueueParsimonyRequests);
            Assert.Empty(fake.AssessRequests);
        }
        finally { await context.StopPageWorkAsync(); }
    }

    [Fact]
    public async Task AnAutomaticCommandFaultIsObservedAndShownForTheCurrentProject()
    {
        var fake = Client();
        fake.EnqueueParsimonyHandler = (_, _) => Task.FromException<CommandOutcome<JobEnqueuedResponse>>(
            new IOException("The queue could not be read."));
        var (context, page) = Build(fake, () => First);
        try
        {
            await context.OpenProjectAsync(ProjectA);
            await page.AutomaticWork;
            Assert.Equal(ParsimonyPageState.Refused, page.State);
            Assert.Equal("The queue could not be read.", page.Message);
            Assert.Empty(fake.AssessRequests);
        }
        finally { await context.StopPageWorkAsync(); }
    }

    [Fact]
    public async Task AFailedJobShowsTheLocalizedRetryMessage()
    {
        var fake = Client();
        fake.EnqueueParsimonyHandler = (_, _) => Task.FromResult(Queued("job/failed"));
        fake.OnWaitForJob((request, _, _) => Task.FromResult(CommandOutcome<JobStatusResponse>.Success(
            new JobStatusResponse(request.JobId, "project", true, "parsimony-report", JobStatus.Failed,
                1, null, false, null, 1))));
        var (context, page) = Build(fake, () => First);
        try
        {
            await context.OpenProjectAsync(ProjectA);
            await page.AutomaticWork;
            Assert.Equal(ParsimonyPageState.Refused, page.State);
            Assert.Equal(MotifAppStrings.ParsimonyJobFailed, page.Message);
            Assert.Empty(fake.AssessRequests);
        }
        finally { await context.StopPageWorkAsync(); }
    }

    private static FakeCommandClient Client()
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(First, null, false));
        fake.OnReadLatestParsimony((_, _) => Task.FromResult(CommandOutcome<ParsimonyLatestReportResponse>.Refused(
            new Refusal(ParsimonyRefusalCodes.NoReport, FailureReason.NotFound, "No report."))));
        fake.ParsimonyViewHandler = (request, _) => View(request,
            [Report(Second, "report/second", "second"), Report(Second, "report/current", "current")]);
        return fake;
    }

    private static Task<CommandOutcome<ParsimonyNamedViewResponse>> View(ReadParsimonyViewRequest request,
        IReadOnlyList<ParsimonyReportResponse> reports)
    {
        var report = reports.FirstOrDefault(report => report.ReportId == request.Query.Filters.ReportId);
        ParsimonyViewRow[] rows = request.Query.View == ParsimonyViews.ParsimonyActiveFindings && report is not null
            ? [.. report.Findings.Select(finding => (ParsimonyViewRow)new ParsimonyFindingDispositionViewRow(report.ReportId,
                report.Inputs.BundleId, "active", finding, null, null, null, null, null, null))] : [];
        return Task.FromResult(CommandOutcome<ParsimonyNamedViewResponse>.Success(new ParsimonyNamedViewResponse(
            request.Query.BundleId, request.Query.View, 1, ParsimonyMeasureStatus.Computed, [], rows.Length,
            rows.Length, null, false, rows, null, null)));
    }

    private static (WorkspaceContext Context, ParsimonyPageModel Page) Build(FakeCommandClient fake, Func<BaselineToken?> token)
    {
        var selection = new SelectionViewModel(fake);
        var context = new WorkspaceContext(selection, new AssessViewModel(fake, selection), new ChangesViewModel(fake),
            fake, new FolderPicker(), new DragSource(), new BaselineViewModel(fake)) { AdvancedAiModeEnabled = true };
        _ = new LoadedBaseline(context, token);
        return (context, new ParsimonyPageModel(context));
    }

    private sealed class LoadedBaseline(WorkspaceContext context, Func<BaselineToken?> token) : PageModel(context)
    {
        protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken)
        {
            Context.Baseline = token() is { } loaded ? Baseline(loaded) : null;
            return Task.CompletedTask;
        }
    }

    private static WorkspaceBaseline Baseline(BaselineToken token) => new(true, "captured", "saved", "now", "free", null)
        { Token = token };
    private static BaselineToken Token(char digit) => new("project", "sha256:" + new string(digit, 64), "projection", "2026-10-23T00:00:00Z",
        "sha256:" + new string('9', 64));
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static CommandOutcome<JobEnqueuedResponse> Queued(string id) =>
        CommandOutcome<JobEnqueuedResponse>.Success(new JobEnqueuedResponse(id, "parsimony-report", "project"));
    private static CommandOutcome<JobStatusResponse> Cancelled() => CommandOutcome<JobStatusResponse>.Refused(
        new Refusal("job.wait-cancelled", FailureReason.Cancelled, "Cancelled."));
    private static CommandOutcome<JobStatusResponse> Completed(string id) => CommandOutcome<JobStatusResponse>.Success(
        new JobStatusResponse(id, "project", true, "parsimony-report", JobStatus.Completed, 1, null, false, null, 1));
    private static ParsimonyReportSetResponse Set(string id, params ParsimonyReportResponse[] reports) =>
        new("job/" + id, "bundle", reports);

    private static ParsimonyReportResponse Report(BaselineToken token, string id, string findingId, string measure = Measure)
    {
        var definition = MeasureCatalog.Find(measure)!;
        var finding = new ParsimonyFinding(findingId, measure, definition.Axis, definition.Tier,
            new ParsimonyFindingAttachment(ParsimonyAttachmentKind.AuthoredObject, "object/" + findingId,
                ParsimonyAuthoredObjectKind.AdhocProhibition), null, new ParsimonyMeasureNumber(1, 2, "prohibitions"),
            definition.Threshold!, "sha256:" + new string('7', 64),
            [new ParsimonyEvidenceReference("bundle", "adhoc-context", JsonSerializer.SerializeToElement(new { }))],
            "guide:parsimony/recipes/test", [], ParsimonyVerification.NotRun);
        return new ParsimonyReportResponse(id, new ParsimonyReportInputs("bundle", token, "baseline", null,
            "model", new ParsimonyArtifactDigest(4, new string('4', 64)), new ParsimonyArtifactDigest(1, new string('5', 64)),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved), [finding], "review")
        {
            Notes = [],
            MeasureRuns = [new ParsimonyMeasureRun(measure, ParsimonyMeasureStatus.Computed, 2, 1, "prohibitions",
                new ParsimonyMeasureNumber(1, 2, "prohibitions"), 0.5, null)],
        };
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }
    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths,
            DragDropEffects allowedEffects) => Task.FromResult(allowedEffects);
    }
}
