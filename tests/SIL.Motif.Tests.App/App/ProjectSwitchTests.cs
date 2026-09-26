using System.Reflection;
using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProjectSwitchTests
{
    private const string ProjectA = @"C:\projects\one.fwdata";
    private const string ProjectB = @"C:\projects\two.fwdata";
    private const string TextId = "11111111-1111-1111-1111-111111111111";
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string BundleDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static readonly Guid TextGuid = Guid.Parse(TextId);

    private static BaselineToken NewToken() =>
        new("project-1", Digest, "1", "2026-09-05T00:00:00Z", BundleDigest);

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
        await OpenProjectAsync(parts.Workspace, ProjectA);
        parts.Workspace.Context.AppliedSinceRefresh = true;

        await OpenProjectAsync(parts.Workspace, ProjectB);

        Assert.False(parts.Workspace.Context.AppliedSinceRefresh);
        Assert.NotEqual("Numbers need refresh", parts.Workspace.FreshnessLabel);
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
        parts.Fake.MeasurePendingCompletesWith(new MeasurePendingResult("job/two", "revision/one", "complete", true));
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
    public async Task SwitchingAwayAndBackKeepsTheStoredDraft()
    {
        var fake = new FakeCommandClient();
        var (client, store) = ProjectScopedCommandClient.Create(fake);
        var parts = NewWorkspace(fake, client);
        await OpenProjectAsync(parts.Workspace, ProjectA);
        await parts.Workspace.Context.Changes.PutAsync(new ChangeIntent(
            "change-a", ChangeKinds.IncorrectSpelling, "wordform-a", "from-a"));

        await OpenProjectAsync(parts.Workspace, ProjectB);
        Assert.Empty(parts.Workspace.Context.Changes.Items);
        await OpenProjectAsync(parts.Workspace, ProjectA);
        Assert.Equal("change-a", Assert.Single(parts.Workspace.Context.Changes.Items).ChangeId);

        fake.OnGetCurrentBaseline((request, cancellationToken) => request.ProjectPath == ProjectB
            ? Task.FromException<CommandOutcome<CurrentBaselineResponse>>(new InvalidOperationException("read failed"))
            : Task.FromResult(CommandOutcome<CurrentBaselineResponse>.Success(
                new CurrentBaselineResponse(NewToken(), DateTimeOffset.UtcNow, false))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => OpenProjectAsync(parts.Workspace, ProjectB));
        await OpenProjectAsync(parts.Workspace, ProjectA);
        Assert.Equal("change-a", Assert.Single(parts.Workspace.Context.Changes.Items).ChangeId);

        fake.OnGetCurrentBaseline((_, cancellationToken) => cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<CommandOutcome<CurrentBaselineResponse>>(cancellationToken)
            : Task.FromResult(CommandOutcome<CurrentBaselineResponse>.Success(
                new CurrentBaselineResponse(NewToken(), DateTimeOffset.UtcNow, false))));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            parts.Workspace.SetProjectAsync(ProjectB, cancellation.Token));
        await OpenProjectAsync(parts.Workspace, ProjectA);

        Assert.Equal("change-a", Assert.Single(parts.Workspace.Context.Changes.Items).ChangeId);
        Assert.DoesNotContain(store.Removes, request => request.FwDataPath == ProjectA);
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
        parts.Workspace.Context.AppliedSinceRefresh = true;
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
                clearedBeforeBaselineLoad = !parts.Workspace.Context.AppliedSinceRefresh &&
                    parts.Workspace.Context.GrammarSummary?.SummaryText == "Not checked yet" &&
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

    public class ProjectScopedCommandClient : DispatchProxy
    {
        private static readonly PendingChangesSnapshot Empty = new(null, "none", [], []);
        private ICommandClient _inner = null!;

        public Dictionary<string, PendingChangesSnapshot> Projects { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<RemovePendingChangeRequest> Removes { get; } = [];

        public static (ICommandClient Client, ProjectScopedCommandClient Store) Create(ICommandClient inner)
        {
            var client = DispatchProxy.Create<ICommandClient, ProjectScopedCommandClient>();
            var store = (ProjectScopedCommandClient)(object)client;
            store._inner = inner;
            return (client, store);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ICommandClient.LoadPendingChangesAsync))
            {
                var request = (PendingChangesRequest)args![0]!;
                return Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Success(
                    Projects.GetValueOrDefault(request.FwDataPath) ?? Empty));
            }
            if (targetMethod?.Name == nameof(ICommandClient.PutPendingChangeAsync))
            {
                var request = (PutPendingChangeRequest)args![0]!;
                var existing = Projects.GetValueOrDefault(request.FwDataPath) ?? Empty;
                var change = request.Change;
                var changes = existing.Changes.Where(item => item.ChangeId != change.ChangeId).Append(
                    new PendingChange(change.ChangeId, change.WordformId, change.Word, change.Kind,
                        change.AssessmentId, change.DisplayReading, [change.ChangeId])
                    { OriginPage = change.OriginPage }).ToArray();
                var snapshot = new PendingChangesSnapshot(existing.DraftId ?? $"draft/{request.FwDataPath}",
                    Guid.NewGuid().ToString("N"), changes, changes.Select(item => new ChangeFit(item.ChangeId, true, [])).ToArray());
                Projects[request.FwDataPath] = snapshot;
                return Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Success(snapshot));
            }
            if (targetMethod?.Name == nameof(ICommandClient.RemovePendingChangeAsync))
            {
                var request = (RemovePendingChangeRequest)args![0]!;
                Removes.Add(request);
                var existing = Projects.GetValueOrDefault(request.FwDataPath) ?? Empty;
                var changes = existing.Changes.Where(item => item.ChangeId != request.ChangeId).ToArray();
                var snapshot = existing with
                {
                    Revision = Guid.NewGuid().ToString("N"),
                    Changes = changes,
                    FitSummary = existing.FitSummary.Where(item => item.ChangeId != request.ChangeId).ToArray(),
                };
                Projects[request.FwDataPath] = snapshot;
                return Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Success(snapshot));
            }
            return targetMethod!.Invoke(_inner, args);
        }
    }
}
