using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProjectSwitchTests
{
    private const string ProjectA = @"C:\projects\one.fwdata";
    private const string ProjectB = @"C:\projects\two.fwdata";
    private const string ProjectC = @"C:\projects\three.fwdata";
    private const string TextId = "11111111-1111-1111-1111-111111111111";
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string BundleDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string OtherTextId = "22222222-2222-2222-2222-222222222222";

    private static readonly Guid TextGuid = Guid.Parse(TextId);

    private static BaselineToken NewToken(string capturedUtc = "2026-09-05T00:00:00Z") =>
        new("project-1", Digest, "1", capturedUtc, BundleDigest);

    private static WorkspaceParts NewWorkspace(FakeCommandClient? fake = null, ICommandClient? client = null)
    {
        fake ??= new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(NewToken(), DateTimeOffset.UtcNow, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse(
            [new TextChoiceSummary(TextGuid, "Alpha")], HasBaseline: true));
        var projectPicker = new FakeProjectPicker();
        var commands = client ?? fake;
        var selection = new SelectionViewModel(commands);
        var workspace = new HandoffWorkspaceViewModel(
            new ProjectViewModel(commands, projectPicker), new BaselineViewModel(commands), selection,
            new AssessViewModel(commands, selection), new FakeFolderPicker(), new FakeDragSource(), commands);
        return new WorkspaceParts(fake, projectPicker, workspace);
    }

    private static Task OpenProjectAsync(HandoffWorkspaceViewModel workspace, string path) =>
        workspace.SetProjectAsync(path);

    [Fact]
    public async Task ApplyingInOneProjectDoesNotMarkTheNextOneStale()
    {
        var parts = NewWorkspace();
        parts.Fake.PendingChangesIs(PendingWithChange("apply-in-a"));
        await OpenProjectAsync(parts.Workspace, ProjectA);
        var review = parts.Workspace.PageModel<ReviewPageModel>();
        parts.Fake.MeasurePendingCompletesWith(new MeasurePendingResult(
            "job/one", "revision/one", FakeCommandClient.CompleteNumbers));
        parts.Fake.ApplyPendingCompletesWith(new ApplyProjection("draft/one", false, "Applied", [], "sha256:effect",
            new AppliedLogEntrySummary("draft/one", "2026-01-01", "Motif", "sha256:intent")));
        await review.MeasureCommand.ExecuteAsync(null);
        await review.ApplyCommand.ExecuteAsync(null);
        Assert.True(parts.Workspace.Context.Evidence.AppliedSinceRefresh);

        await OpenProjectAsync(parts.Workspace, ProjectB);

        Assert.False(parts.Workspace.Context.Evidence.AppliedSinceRefresh);
        Assert.NotEqual("Numbers need refresh", parts.Workspace.FreshnessLabel);
    }

    [Fact]
    public async Task ASecondSwitchBeforeTheFirstFinishesShowsOnlyTheSecondProject()
    {
        var parts = NewWorkspace();
        var bTextId = Guid.Parse(OtherTextId);
        var cTextId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var bStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseB = new TaskCompletionSource<CommandOutcome<TextInventoryResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        parts.Fake.OnListTexts((request, _) => request.ProjectPath == ProjectB
            ? HoldB()
            : Task.FromResult(CommandOutcome<TextInventoryResponse>.Success(new TextInventoryResponse(
                [new TextChoiceSummary(request.ProjectPath == ProjectA ? TextGuid : cTextId,
                    request.ProjectPath == ProjectA ? "Alpha" : "Charlie")], HasBaseline: true))));
        parts.Fake.PendingLoadHandler = (request, _) => Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Success(
            request.FwDataPath == ProjectB ? PendingWithChange("from-b") :
            request.FwDataPath == ProjectC ? PendingWithChange("from-c") : PendingWithChange("from-a")));
        parts.Fake.DefaultSelectionHandler = (request, _) => Task.FromResult(
            CommandOutcome<DefaultSelectionResponse>.Success(new DefaultSelectionResponse(
                new NamedSelectionProjection("Default", [], [], "created", "updated",
                    request.ProjectPath == ProjectC ? 2300 : 1100,
                    request.ProjectPath == ProjectC ? new StepCap(2300) : new StepCap(1100)))));

        await OpenProjectAsync(parts.Workspace, ProjectA);
        var openingB = OpenProjectAsync(parts.Workspace, ProjectB);
        await bStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await OpenProjectAsync(parts.Workspace, ProjectC);
        releaseB.SetResult(CommandOutcome<TextInventoryResponse>.Success(new TextInventoryResponse(
            [new TextChoiceSummary(bTextId, "Bravo")], HasBaseline: true)));
        await openingB;

        Assert.Equal(ProjectC, parts.Workspace.Context.Setup!.ProjectPath);
        Assert.Equal("Charlie", Assert.Single(parts.Workspace.Selection.Texts).Title);
        Assert.Equal(2.3m, parts.Workspace.Selection.PerWordTimeLimitSeconds);
        Assert.Equal(2300m, parts.Workspace.Selection.PerWordStepLimit);
        Assert.Equal("from-c", Assert.Single(parts.Workspace.Context.Changes.Items).ChangeId);

        Task<CommandOutcome<TextInventoryResponse>> HoldB()
        {
            bStarted.TrySetResult();
            return releaseB.Task;
        }
    }

    [Fact]
    public async Task AnOldProjectsPendingAnswerThatLandsAfterTheSwitchIsDropped()
    {
        var parts = NewWorkspace();
        await OpenProjectAsync(parts.Workspace, ProjectA);
        var putStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePut = new TaskCompletionSource<CommandOutcome<PendingChangesSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        parts.Fake.PendingPutHandler = (request, _) =>
        {
            Assert.Equal(ProjectA, request.FwDataPath);
            putStarted.TrySetResult();
            return releasePut.Task;
        };

        var put = parts.Workspace.Context.Changes.PutAsync(new ChangeIntent(
            "late-a", ChangeKinds.IncorrectSpelling, "wordform-a", "from-a"));
        await putStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await OpenProjectAsync(parts.Workspace, ProjectB);
        var snapshotInB = parts.Workspace.Context.Changes.Snapshot;

        releasePut.SetResult(CommandOutcome<PendingChangesSnapshot>.Success(PendingWithChange("late-a")));
        await put;

        Assert.Same(snapshotInB, parts.Workspace.Context.Changes.Snapshot);
        Assert.Empty(parts.Workspace.Context.Changes.Items);
    }

    [Fact]
    public async Task AnOldBulkCollectionRefusalDoesNotLeakAcrossAProjectSwitch()
    {
        var parts = NewWorkspace();
        await OpenProjectAsync(parts.Workspace, ProjectA);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<CommandOutcome<PendingChangesSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var refusal = new Refusal("change.cannot-compose", FailureReason.Refused, "old project refusal");
        parts.Fake.PendingLoadHandler = (request, _) => Task.FromResult(
            CommandOutcome<PendingChangesSnapshot>.Success(request.FwDataPath == ProjectB
                ? PendingWithChange("from-b")
                : new PendingChangesSnapshot(null, "none", [], [])));
        var calls = 0;
        parts.Fake.PendingPutHandler = (_, _) => Interlocked.Increment(ref calls) == 1
            ? Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Refused(refusal))
            : WaitForSecondPut();

        var result = new AssessmentWordResult("word", "analysed", false, "Done", 1, null)
        {
            Morphology = new ParseWordEvidence("v1", 0, "word", 1, false, false, false,
                [new ParseAnalysis([new ParseMorph(null, null, null, "first")]),
                 new ParseAnalysis([new ParseMorph(null, null, null, "second")])], []),
        };
        var word = new CompareWordViewModel(new AssessWordRowViewModel(result),
            (WordProjectStatus.NotPresent, CompareColumnKind.NoMatch));
        var adding = parts.Workspace.Context.Changes.AddAsync(ChangeKinds.AddCandidate, word);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await OpenProjectAsync(parts.Workspace, ProjectB);
        release.SetResult(CommandOutcome<PendingChangesSnapshot>.Success(PendingWithChange("from-b")));
        await adding;

        Assert.Equal(ProjectB, parts.Workspace.Context.Changes.ProjectPath);
        Assert.Null(parts.Workspace.Context.Changes.LastRefusal);
        Assert.Equal("from-b", Assert.Single(parts.Workspace.Context.Changes.Items).ChangeId);

        Task<CommandOutcome<PendingChangesSnapshot>> WaitForSecondPut()
        {
            started.TrySetResult();
            return release.Task;
        }
    }

    [Fact]
    public async Task ARefreshInFlightDoesNotLandInTheNextProject()
    {
        var parts = NewWorkspace();
        parts.Fake.DefaultSelectionResponseIs(System.Text.Json.JsonSerializer.Deserialize<DefaultSelectionResponse>("""
            {"Selection":{"Name":"Default","TextIds":["11111111-1111-1111-1111-111111111111"],
             "AddedWords":[],"CreatedUtc":"created","UpdatedUtc":"updated",
             "PerWordLimitMs":1000,"PerWordStepLimit":{"steps":50000000,"isUnbounded":false}},"SetupSkipped":false}
            """)!);
        var tokenA = NewToken("2026-09-05T00:00:00Z");
        var tokenB = NewToken("2026-09-06T00:00:00Z");
        parts.Fake.OnGetCurrentBaseline((request, _) => Task.FromResult(
            CommandOutcome<CurrentBaselineResponse>.Success(new CurrentBaselineResponse(
                request.ProjectPath == ProjectB ? tokenB : tokenA, DateTimeOffset.UtcNow, false))));
        await OpenProjectAsync(parts.Workspace, ProjectA);
        parts.Workspace.Selection.AllWordforms = true;
        var captureStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCapture = new TaskCompletionSource<CommandOutcome<BaselineCaptureResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        parts.Fake.OnCaptureBaseline((request, _) =>
        {
            Assert.Equal(ProjectA, request.ProjectPath);
            captureStarted.TrySetResult();
            return releaseCapture.Task;
        });

        var refresh = parts.Workspace.RefreshCommand.ExecuteAsync(null);
        await captureStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(parts.Workspace.ProjectSwitchEnabled);
        await OpenProjectAsync(parts.Workspace, ProjectB);
        releaseCapture.SetResult(CommandOutcome<BaselineCaptureResponse>.Success(new BaselineCaptureResponse(
            NewToken("2026-09-07T00:00:00Z"), ProjectA, DateTimeOffset.UtcNow, false, false)));
        await refresh;

        Assert.Equal(tokenB, parts.Workspace.Baseline.Token);
        Assert.Empty(parts.Fake.AssessRequests);
        Assert.False(parts.Workspace.RerunOffered);
    }

    [Fact]
    public async Task StoredNumbersStillOfferARerunWhenTheBaselineAnswersLast()
    {
        var parts = NewWorkspace();
        var baselineStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBaseline = new TaskCompletionSource<CommandOutcome<CurrentBaselineResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        parts.Fake.OnGetCurrentBaseline((_, _) =>
        {
            baselineStarted.TrySetResult();
            return releaseBaseline.Task;
        });

        var opening = OpenProjectAsync(parts.Workspace, ProjectA);
        await baselineStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        parts.Fake.ReadCurrentEvidenceCompletesWith(new CurrentEvidenceSnapshot("one", DateTimeOffset.UtcNow,
            null, EvidenceFreshness.Current,
            new BaselineRecord("project-1", NewToken(), "root", ProjectA,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), null, null, null, StoredAssessment()));
        releaseBaseline.SetResult(CommandOutcome<CurrentBaselineResponse>.Success(
            new CurrentBaselineResponse(NewToken(), DateTimeOffset.UtcNow, false)));
        await opening;
        Assert.Contains(ProjectA, parts.Fake.CurrentEvidenceRequests);
        Assert.True(parts.Workspace.Baseline.HasAssessment);
        parts.Fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            NewToken("2026-09-06T00:00:00Z"), ProjectA, DateTimeOffset.UtcNow, false, false));

        await parts.Workspace.Baseline.RefreshCommand.ExecuteAsync(null);

        Assert.True(parts.Workspace.RerunOffered);
    }

    [Fact]
    public async Task NoProjectReadStartsBeforeTheBaselineAnswers()
    {
        var parts = NewWorkspace();
        var baselineStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBaseline = new TaskCompletionSource<CommandOutcome<CurrentBaselineResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var readOrder = new System.Collections.Concurrent.ConcurrentQueue<(string Kind, string Path)>();
        parts.Fake.OnGetCurrentBaseline((request, _) =>
        {
            if (request.ProjectPath == ProjectB)
            {
                baselineStarted.TrySetResult();
                return releaseBaseline.Task;
            }
            return Task.FromResult(CommandOutcome<CurrentBaselineResponse>.Success(
                new CurrentBaselineResponse(NewToken(), DateTimeOffset.UtcNow, false)));
        });
        parts.Fake.CurrentEvidenceHandler = (path, _) =>
        {
            readOrder.Enqueue(("current-evidence", path));
            return Task.FromResult(CommandOutcome<CurrentEvidenceSnapshot>.Success(
                new CurrentEvidenceSnapshot("two", DateTimeOffset.UtcNow, null,
                    EvidenceFreshness.NoBaseline, null, null, null, null, null)));
        };
        parts.Fake.OnListTexts((request, _) =>
        {
            readOrder.Enqueue(("list-texts", request.ProjectPath));
            return Task.FromResult(CommandOutcome<TextInventoryResponse>.Success(
                new TextInventoryResponse([], HasBaseline: true)));
        });
        parts.Fake.PendingLoadHandler = (request, _) =>
        {
            readOrder.Enqueue(("pending-changes", request.FwDataPath));
            return Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Success(
                new PendingChangesSnapshot(null, "none", [], [])));
        };
        parts.Fake.DefaultSelectionHandler = (request, _) =>
        {
            readOrder.Enqueue(("default-selection", request.ProjectPath));
            return Task.FromResult(CommandOutcome<DefaultSelectionResponse>.Success(
                new DefaultSelectionResponse(null)));
        };

        var opening = OpenProjectAsync(parts.Workspace, ProjectB);
        await baselineStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.DoesNotContain(parts.Fake.CurrentEvidenceRequests, path => path == ProjectB);
        Assert.DoesNotContain(parts.Fake.ListTextsRequests, request => request.ProjectPath == ProjectB);
        Assert.DoesNotContain(parts.Fake.PendingLoadRequests, request => request.FwDataPath == ProjectB);
        Assert.DoesNotContain(parts.Fake.DefaultSelectionRequests, request => request.ProjectPath == ProjectB);

        releaseBaseline.SetResult(CommandOutcome<CurrentBaselineResponse>.Success(
            new CurrentBaselineResponse(NewToken(), DateTimeOffset.UtcNow, false)));
        await opening;

        var projectReads = readOrder.Where(read => read.Path == ProjectB).ToArray();
        var setupIndex = Array.FindIndex(projectReads, read => read.Kind == "default-selection");
        Assert.True(setupIndex >= 0);
        Assert.Contains(projectReads, read => read.Kind == "current-evidence");
        Assert.Contains(projectReads, read => read.Kind == "list-texts");
        Assert.Contains(projectReads, read => read.Kind == "pending-changes");
        Assert.All(projectReads.Select((read, index) => (read, index))
            .Where(item => item.read.Kind is "current-evidence" or "list-texts" or "pending-changes"),
            item => Assert.True(item.index < setupIndex));
    }

    [Fact]
    public async Task NoPageKeepsTheOldProjectsInputsAfterASwitch()
    {
        var parts = NewWorkspace();
        await OpenProjectAsync(parts.Workspace, ProjectA);
        var handoff = parts.Workspace.PageModel<AiHandoffPageModel>().Handoff;
        handoff.UseWords(["from-a"]);
        handoff.CoverageText = "Covers A";
        handoff.LatestAssessmentAt = DateTimeOffset.UtcNow;
        handoff.WrittenAt = DateTimeOffset.UtcNow;
        var tryWord = parts.Workspace.PageModel<TryWordPageModel>();
        tryWord.Trace.WordToTry = "from-a";
        var texts = parts.Workspace.PageModel<TextsPageModel>();
        texts.Words.SearchText = "from-a";
        texts.Words.StatusFilter = WordProjectStatus.NotPresent;
        texts.Words.SeveralOnly = true;
        texts.ResultsInText.SelectedText = new ResultsTextViewModel(
            new TextLines(TextGuid, "Alpha", []), new Dictionary<string, AssessmentWordResult>());
        texts.Tab = TextsTab.Lists;
        texts.AnalyzeView = AnalyzeTextsView.WordList;
        texts.ResultsInText.Filter = ResultsInTextFilter.New;

        await OpenProjectAsync(parts.Workspace, ProjectB);

        Assert.Null(handoff.ChosenWords);
        Assert.Null(handoff.CoverageText);
        Assert.Null(handoff.LatestAssessmentAt);
        Assert.Null(handoff.WrittenAt);
        Assert.Equal(string.Empty, tryWord.Trace.WordToTry);
        Assert.Equal(string.Empty, texts.Words.SearchText);
        Assert.Null(texts.Words.StatusFilter);
        Assert.False(texts.Words.SeveralOnly);
        Assert.Null(texts.ResultsInText.SelectedText);
        Assert.Equal(TextsTab.Lists, texts.Tab);
        Assert.Equal(AnalyzeTextsView.WordList, texts.AnalyzeView);
        Assert.Equal(ResultsInTextFilter.New, texts.ResultsInText.Filter);
    }

    [Fact]
    public async Task OpeningAProjectAfterStoppingWorkRefreshesRecentProjects()
    {
        var parts = NewWorkspace();
        parts.Workspace.Project.KnownProjects.Add(new KnownProjectSummary(ProjectA, DateTimeOffset.UtcNow));
        parts.Workspace.Project.KnownProjects.Add(new KnownProjectSummary(ProjectB, DateTimeOffset.UtcNow));
        await OpenProjectAsync(parts.Workspace, ProjectA);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        parts.Fake.OnAssess((_, _, cancellationToken) =>
        {
            started.TrySetResult();
            var completion = new TaskCompletionSource<CommandOutcome<AssessCommandResponse>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetResult(CommandOutcome<AssessCommandResponse>.Refused(
                new Refusal("assess.cancelled", FailureReason.Cancelled, "cancelled"))));
            return completion.Task;
        });
        parts.Workspace.Selection.AllWordforms = true;
        var assessment = parts.Workspace.Assess.RunCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await OpenProjectAsync(parts.Workspace, ProjectB);
        await assessment;

        Assert.Contains(parts.Workspace.RecentProjects, recent => recent.FullFwDataPath == ProjectA);
        Assert.DoesNotContain(parts.Workspace.RecentProjects, recent => recent.FullFwDataPath == ProjectB);
    }

    [Fact]
    public async Task AReviewErrorDoesNotFollowTheNextProject()
    {
        var parts = NewWorkspace();
        var review = parts.Workspace.PageModel<ReviewPageModel>();
        parts.Fake.PendingChangesIs(PendingWithChange("first"));
        await OpenProjectAsync(parts.Workspace, ProjectA);

        parts.Fake.MeasurePendingRefusal = new Refusal("job.wait-cancelled", FailureReason.Cancelled, "cancelled");
        await review.MeasureCommand.ExecuteAsync(null);
        Assert.NotNull(review.MeasurementError);

        await OpenProjectAsync(parts.Workspace, ProjectB);

        Assert.Null(review.MeasurementError);
        parts.Fake.PendingChangesIs(PendingWithChange("second"));
        await parts.Workspace.Context.Changes.ReloadAsync();
        parts.Fake.MeasurePendingRefusal = null;
        parts.Fake.MeasurePendingCompletesWith(new MeasurePendingResult("job/two", "revision/one", FakeCommandClient.CompleteNumbers));
        parts.Fake.ApplyPendingRefusal = new Refusal("apply.regression", FailureReason.Refused, "regression");
        await review.MeasureCommand.ExecuteAsync(null);
        await review.ApplyCommand.ExecuteAsync(null);
        Assert.NotNull(review.ApplyError);

        await OpenProjectAsync(parts.Workspace, ProjectA);

        Assert.Null(review.ApplyError);
    }

    [Fact]
    public async Task NoPendingChangeOfTheOldProjectIsVisibleOrWritableDuringTheSwitch()
    {
        var parts = NewWorkspace();
        parts.Fake.PendingChangesIs(PendingWithChange("from-a"));
        await OpenProjectAsync(parts.Workspace, ProjectA);
        parts.Fake.PendingChangesIs(new PendingChangesSnapshot(null, "none", [], []));
        var textLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishTextLoad = new TaskCompletionSource<CommandOutcome<TextInventoryResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        parts.Fake.OnListTexts((request, _) =>
        {
            if (request.ProjectPath == ProjectB)
            {
                textLoadStarted.TrySetResult();
                return finishTextLoad.Task;
            }
            return Task.FromResult(CommandOutcome<TextInventoryResponse>.Success(
                new TextInventoryResponse([new TextChoiceSummary(TextGuid, "Alpha")], HasBaseline: true)));
        });

        var switching = OpenProjectAsync(parts.Workspace, ProjectB);
        try
        {
            await textLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotEqual(ProjectA, parts.Workspace.Context.Changes.ProjectPath);
            Assert.DoesNotContain(parts.Workspace.Context.Changes.Items, item => item.ChangeId == "from-a");
            await parts.Workspace.Context.Changes.PutAsync(new ChangeIntent(
                "during-switch", ChangeKinds.IncorrectSpelling, "wordform", "during switch"));
            Assert.Equal(ProjectB, Assert.Single(parts.Fake.PendingPutRequests).FwDataPath);
        }
        finally
        {
            finishTextLoad.TrySetResult(CommandOutcome<TextInventoryResponse>.Success(
                new TextInventoryResponse([new TextChoiceSummary(TextGuid, "Alpha")], HasBaseline: true)));
            await switching;
        }
    }

    [Fact]
    public async Task EveryParticipantIsClearedBeforeAnyLoads()
    {
        var parts = NewWorkspace();
        var review = parts.Workspace.PageModel<ReviewPageModel>();
        var calls = new List<string>();
        var probe = new ProbePageModel(parts.Workspace.Context, calls);
        parts.Fake.PendingChangesIs(PendingWithChange("first"));
        await OpenProjectAsync(parts.Workspace, ProjectA);
        parts.Workspace.Context.RecordApplied();
        parts.Workspace.Context.GrammarSummary = new GrammarSummary("1 warning", true, "warning");
        parts.Workspace.Context.OpenPage(WorkspacePage.Timing);
        parts.Workspace.Context.Changes.Items.Add(new ChangeViewModel(ChangeKinds.IncorrectSpelling, "old", ""));
        parts.Fake.MeasurePendingRefusal = new Refusal("job.wait-cancelled", FailureReason.Cancelled, "cancelled");
        await review.MeasureCommand.ExecuteAsync(null);
        calls.Clear();
        var clearedBeforeBaselineLoad = false;
        parts.Fake.OnGetCurrentBaseline((request, _) =>
        {
            if (request.ProjectPath == ProjectB)
            {
                calls.Add("baseline-load");
                clearedBeforeBaselineLoad = !parts.Workspace.Context.Evidence.AppliedSinceRefresh &&
                    parts.Workspace.Context.GrammarSummary?.SummaryText != "1 warning" &&
                    parts.Workspace.Context.CurrentPage == WorkspacePage.Overview &&
                    parts.Workspace.Context.Changes.ProjectPath is null &&
                    parts.Workspace.Context.Changes.Items.Count == 0 &&
                    parts.Workspace.Selection.Texts.Count == 0 &&
                    parts.Workspace.Context.Setup?.ProjectPath is null &&
                    parts.Workspace.Baseline.Token is null &&
                    parts.Workspace.Context.Baseline is null &&
                    review.MeasurementError is null && calls.Contains("probe-clear");
            }
            return Task.FromResult(CommandOutcome<CurrentBaselineResponse>.Success(
                new CurrentBaselineResponse(NewToken(), DateTimeOffset.UtcNow, false)));
        });

        await OpenProjectAsync(parts.Workspace, ProjectB);

        Assert.True(clearedBeforeBaselineLoad);
        Assert.True(calls.IndexOf("probe-clear") < calls.IndexOf("baseline-load"));
        Assert.Contains("probe-load", calls);
        GC.KeepAlive(probe);
    }

    private static PendingChangesSnapshot PendingWithChange(string id) => new("draft/one", "revision/one",
        [new PendingChange(id, "wordform", id, ChangeKinds.IncorrectSpelling, null, null, [id])],
        [new ChangeFit(id, true, [])]);

    private static AssessmentRecord StoredAssessment() => new(
        "assessment-1", null, null, "pangloss", AssessmentKind.ParseTime.ToStoredKind(), "{}", "sha256:scope",
        "whitespace", "1", "{}", Selection.Create("Default", ["dogs"]), null, null,
        "sha256:grammar", null, null, null, "2026-09-24T12:00:00.0000000+00:00", Words: []);

    private sealed record WorkspaceParts(FakeCommandClient Fake, FakeProjectPicker ProjectPicker,
        HandoffWorkspaceViewModel Workspace);

    private sealed class ProbePageModel(WorkspaceContext context, List<string> calls) : PageModel(context)
    {
        protected override void OnProjectCleared() => calls.Add("probe-clear");

        protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken)
        {
            calls.Add("probe-load");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProjectPicker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FakeFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FakeDragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(DragDropEffects.None);
    }

}
