using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the one <see cref="ProjectEvidence"/> every page reads: stored evidence reaches the pages without any page
/// fetching it, the top bar and the pages agree on freshness, a burst of publications reaches each page once with
/// the latest evidence, and every clock the window reads is the one it was composed with.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProjectEvidenceTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static readonly DateTimeOffset Saved = new(2026, 9, 5, 10, 58, 0, TimeSpan.Zero);

    private static readonly BaselineToken Token = new(
        "project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));

    [Fact]
    public void StoredEvidenceReachesEveryPageWithoutTheOverviewPage()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var context = NewContext(fake);
            var texts = new TextsPageModel(context);
            var timing = new TimingPageModel(context);
            var tryWord = new TryWordPageModel(context);
            fake.ReadCurrentEvidenceCompletesWith(StoredSnapshot());
            fake.TimingCompletesWith(new TimingResponse("assessment-parse", "all", "kind", 1, 5, 8, [], [], []));
            fake.TraceWordCompletesWith(new WordTraceResponse("dogs", true, true, null, 1, null, 10,
                [new TraceCandidate([], true, null, "Built the word", [
                    new TraceStep("MorphologicalRule", "Plural", "dog", "dogs", null, [])])],
                new TraceStep("WordAnalysis", null, null, null, null, [])));

            await context.OpenProjectAsync(ProjectPath);

            Assert.Equal("dogs", Assert.Single(texts.Assess.Words.AllRows).Word);
            Assert.False(texts.ShowEmptyResults);
            Assert.Contains(fake.TimingRequests, request => request.AssessmentId == "assessment-parse" &&
                request.By == "kind");
            Assert.NotNull(timing.KindTiming);
            Assert.False(timing.ShowNoEvidence);

            fake.TimingRequests.Clear();
            context.TryWord("dogs");
            await tryWord.Trace.TryCommand.ExecutionTask!;
            Assert.Contains(fake.TimingRequests, request => request.AssessmentId == "assessment-parse" &&
                request.By == "rule");
        }, TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData("current")]
    [InlineData("fieldworks-saved-since")]
    [InlineData("applied-since")]
    public async Task TheTopBarOverviewAndReviewAgreeOnFreshness(string state)
    {
        var (fake, workspace) = NewWorkspace();
        fake.OverviewCompletesWith(Overview());
        fake.ReadCurrentEvidenceCompletesWith(StoredSnapshot());
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [new PendingChange("kept", "wordform/kept", "dogs", "approve", "assessment/one", "reading", ["op/kept"])],
            [new ChangeFit("kept", true, [])]));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult(
            "job/one", "revision/one", FakeCommandClient.CompleteNumbers));
        await workspace.SetProjectAsync(ProjectPath);
        var overview = workspace.PageModel<OverviewPageModel>();
        var review = workspace.PageModel<ReviewPageModel>();
        await review.MeasureCommand.ExecuteAsync(null);

        switch (state)
        {
            case "fieldworks-saved-since":
                fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, Saved, false)
                {
                    ProjectLastWriteUtc = Saved.AddHours(1),
                });
                await workspace.CheckFreshnessAsync();
                break;
            case "applied-since":
                workspace.Context.RecordApplied();
                break;
        }

        var stale = state != "current";
        Assert.Equal(stale, workspace.FreshnessIsStale);
        Assert.Equal(!stale, workspace.FreshnessIsCurrent);
        Assert.Equal(stale, overview.OverviewIsStale);
        Assert.Equal(!stale, review.CanApply);
        Assert.Equal(stale, review.ApplyBlockReason.Contains("Refresh", StringComparison.Ordinal));
        Assert.Equal(state == "applied-since" ? "Numbers need refresh"
            : stale ? "FieldWorks saved since" : "Current", workspace.FreshnessLabel);
    }

    [Fact]
    public async Task ABurstOfPublicationsReachesEachPageOnceMoreWithTheLatestEvidence()
    {
        var fake = new FakeCommandClient();
        var context = NewContext(fake);
        var probe = new ProbePageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.Hold = release.Task;

        context.PublishEvidence(new WorkspaceEvidence(Run("invocation/first"), Saved, WasRerun: false));
        context.PublishEvidence(new WorkspaceEvidence(Run("invocation/second"), Saved, WasRerun: false));
        context.PublishEvidence(new WorkspaceEvidence(Run("invocation/third"), Saved, WasRerun: false));
        probe.Hold = null;
        release.SetResult();
        await context.EvidencePublication;

        Assert.Equal(["invocation/first", "invocation/third"], probe.Seen);
    }

    [Fact]
    public async Task TheFreshnessSentenceSaysTodayByTheComposedClock()
    {
        var sameDay = new FixedClock(Saved.AddHours(2));
        var (fake, workspace) = NewWorkspace(sameDay);
        fake.ReadCurrentEvidenceCompletesWith(StoredSnapshot());

        await workspace.SetProjectAsync(ProjectPath);
        Assert.Contains(" today", workspace.FreshnessDetail, StringComparison.Ordinal);

        sameDay.Advance(TimeSpan.FromDays(2));
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, Saved, false)
        {
            ProjectLastWriteUtc = Saved.AddHours(1),
        });
        await workspace.CheckFreshnessAsync();
        Assert.DoesNotContain(" today", workspace.FreshnessDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheGrammarCheckReadsTheContextsClock()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 4, 10, 30, 0, TimeSpan.Zero));
        var fake = new FakeCommandClient();
        var context = NewContext(fake, clock);
        var warnings = new WarningsPageModel(context);
        var answer = new TaskCompletionSource<CommandOutcome<GrammarCheckResponse>>();
        fake.OnCheckGrammar((_, _) => answer.Task);
        await context.OpenProjectAsync(ProjectPath);

        var checking = warnings.CheckGrammarCommand.ExecuteAsync(null);
        // Further ahead than the wall clock can get before the wait gives up, so only the composed clock passes.
        clock.Advance(TimeSpan.FromSeconds(90));
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (warnings.Grammar.ElapsedSeconds != 90 && DateTime.UtcNow < deadline) await Task.Delay(50);
        Assert.Equal("90 s", warnings.Grammar.ElapsedText);
        answer.SetResult(CommandOutcome<GrammarCheckResponse>.Success(new GrammarCheckResponse([], HasBaseline: true)));
        await checking;
    }

    [Fact]
    public async Task TheHandoffReadsTheContextsClock()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 4, 10, 30, 0, TimeSpan.Zero));
        var fake = new FakeCommandClient();
        var context = NewContext(fake, clock);
        var handoff = new AiHandoffPageModel(context);
        await context.OpenProjectAsync(ProjectPath);

        context.Selection.AllWordforms = true;
        handoff.Handoff.InvocationId = "invocation/one";
        fake.HandoffCompletesWith(new HandoffCommandResponse(@"C:\out",
            new BaselineCaptureResponse(Token, ProjectPath, Saved, false, true), new SelectionProjection([], []),
            ["handoff.md"], ["assessment-parse"]));
        await handoff.Handoff.RunCommand.ExecuteAsync(null);
        Assert.Equal(clock.GetLocalNow(), handoff.Handoff.WrittenAt);
    }

    private static CurrentEvidenceSnapshot StoredSnapshot() => new("one", Saved, Saved, EvidenceFreshness.Current,
        new BaselineRecord("project-1", Token, "root", ProjectPath, Saved, Saved), null, null, null,
        new AssessmentRecord("assessment-parse", null, null, "pangloss", AssessmentKind.ParseTime.ToStoredKind(),
            "{}", "sha256:scope", "whitespace", "1", "{}", Selection.Create("Default", ["dogs"]), null, null,
            "sha256:grammar", null, null, null, "2026-09-05T11:10:00.0000000+00:00",
            Words: [new AssessedWord("dogs", "analysed", [], 10) { ProjectStanding = ProjectStanding.Approved }])
        {
            Invocation = new BatchInvocationEvidence("invocation/stored", "source", "digest", "digest",
                "words", "digest", "tsv", "digest", "stderr", "digest", 1000,
                SIL.Motif.Contract.Assess.StepCap.Default, 1, false),
        });

    private static AssessCommandResponse Run(string invocationId) => new(
        new BaselineCaptureResponse(Token, ProjectPath, Saved, false, false), new SelectionProjection([], []), [],
        "summary")
    {
        InvocationId = invocationId,
        Measurements = [new ProducedAssessmentReference("assessment-" + invocationId, AssessmentKinds.ParseTime,
            invocationId)],
    };

    private static OverviewResponse Overview() => new(
        "one", Saved, Saved, 1, 0, 1, 0, 1, 0, 0, "assessment-parse", Saved, null, null, null,
        new OverviewTextCoverage(1, 0, 0, 0, 0, 0), new OverviewAccuracy(0, 0, 0, 0, 0, 0, 0, 0),
        new OverviewTiming(null, null, [], 0), null);

    private static WorkspaceContext NewContext(FakeCommandClient fake, TimeProvider? clock = null)
    {
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, Saved, false)
        {
            ProjectLastWriteUtc = Saved,
        });
        var selection = new SelectionViewModel(fake);
        return new WorkspaceContext(selection, new AssessViewModel(fake, selection), new ChangesViewModel(fake), fake,
            new FolderPicker(), new DragSource(), new BaselineViewModel(fake), clock);
    }

    private static (FakeCommandClient Fake, HandoffWorkspaceViewModel Workspace) NewWorkspace(
        TimeProvider? clock = null)
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, Saved, false)
        {
            ProjectLastWriteUtc = Saved,
        });
        fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: true));
        var selection = new SelectionViewModel(fake);
        var workspace = new HandoffWorkspaceViewModel(
            new ProjectViewModel(fake, new ProjectPicker()), new BaselineViewModel(fake), selection,
            new AssessViewModel(fake, selection), new FolderPicker(), new DragSource(), fake, clock);
        return (fake, workspace);
    }

    private sealed class ProbePageModel(WorkspaceContext context) : PageModel(context)
    {
        public List<string> Seen { get; } = [];

        public Task? Hold { get; set; }

        protected override async Task OnEvidencePublishedAsync(
            ProjectEvidence evidence, CancellationToken cancellationToken)
        {
            if (evidence.Assessment is not { } shown) return;
            Seen.Add(shown.Assessment.InvocationId);
            if (Hold is { } hold) await hold;
        }
    }

    private sealed class ProjectPicker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(@"C:\out");
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
