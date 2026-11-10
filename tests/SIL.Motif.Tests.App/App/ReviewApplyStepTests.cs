using Avalonia.Input;
using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Pins Apply as a deliberate step: its summary, checklist, confirmation question and result lines.</summary>
public sealed class ReviewApplyStepTests
{
    private const string ProjectPath = @"C:\projects\Sample.fwdata";
    private const string ProposalId = "proposal/one";

    private static readonly BaselineToken Token = new("project-id", "sha256:" + new string('1', 64),
        "projection-v1", "2026-09-24T10:00:00Z", "sha256:" + new string('a', 64));

    [Fact]
    public async Task TheSummaryCountsTheOperationsTheProposalWritesAndNamesTheProjectFile()
    {
        var (_, _, page) = await Open(Entry("a", 3), Entry("b", 1));

        Assert.Equal("Writes 4 changes to Sample.fwdata", page.ApplySummaryText);
        Assert.Equal("Apply 4 changes to Sample?", page.ApplyQuestionText);
        Assert.Equal(page.ApplySummaryText, page.ApplyCardCountText);
    }

    [Fact]
    public async Task AOneOperationProposalReadsInTheSingular()
    {
        var (_, _, page) = await Open(Entry("a", 1));

        Assert.Equal("Writes 1 change to Sample.fwdata", page.ApplySummaryText);
        Assert.Equal("Apply 1 change to Sample?", page.ApplyQuestionText);
    }

    [Fact]
    public async Task TheChecklistStartsWithFieldWorksBeingClosedAndFollowsTheProjectLock()
    {
        var (_, context, page) = await Open(Entry("a", 1));
        Assert.Equal(["✓ FieldWorks is closed", "✓ Tried as it is now", "✓ Every change still fits"],
            page.ApplyChecks.Select(check => check.MarkedText));

        context.Baseline = new WorkspaceBaseline(true, "", "", "", "", null) { FieldWorksHeldProject = true };

        Assert.Equal("✗ FieldWorks has the project open", page.ApplyChecks[0].MarkedText);
        Assert.False(page.CanApply);
        Assert.False(page.AskToApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task AskingOpensTheQuestionAndWritesNothingUntilItIsConfirmed()
    {
        var (fake, _, page) = await Open(Entry("a", 2));
        Assert.True(page.OffersApply);

        page.AskToApplyCommand.Execute(null);

        Assert.True(page.IsConfirmingApply);
        Assert.False(page.OffersApply);
        Assert.Empty(fake.ApplyProposalRequests);
    }

    [Fact]
    public async Task CancellingTheQuestionWritesNothingAndOffersApplyAgain()
    {
        var (fake, _, page) = await Open(Entry("a", 2));
        page.AskToApplyCommand.Execute(null);

        page.CancelApplyCommand.Execute(null);

        Assert.False(page.IsConfirmingApply);
        Assert.True(page.OffersApply);
        Assert.Empty(fake.ApplyProposalRequests);
    }

    [Fact]
    public async Task AQuestionThatStopsBeingAnswerableClosesItself()
    {
        var (_, context, page) = await Open(Entry("a", 2));
        page.AskToApplyCommand.Execute(null);

        context.Baseline = new WorkspaceBaseline(true, "", "", "", "", null) { FieldWorksHeldProject = true };

        Assert.False(page.IsConfirmingApply);
        Assert.True(page.OffersApply);
    }

    [Fact]
    public async Task AQuestionClosedByABlockerDoesNotReopenWhenTheBlockerClears()
    {
        var (_, context, page) = await Open(Entry("a", 2));
        page.AskToApplyCommand.Execute(null);
        context.Baseline = new WorkspaceBaseline(true, "", "", "", "", null) { FieldWorksHeldProject = true };
        Assert.False(page.IsConfirmingApply);

        context.Baseline = new WorkspaceBaseline(true, "", "", "", "", null) { FieldWorksHeldProject = false };

        Assert.True(page.CanApply);
        Assert.False(page.IsConfirmingApply);
        Assert.True(page.OffersApply);
    }

    [Fact]
    public async Task AQuestionDoesNotSurviveLeavingTheProposal()
    {
        var (_, context, page) = await Open(Entry("a", 2));
        page.AskToApplyCommand.Execute(null);

        await context.LeaveProposalAsync();

        Assert.False(page.IsConfirmingApply);
    }

    [Fact]
    public async Task ConfirmingWritesOnceAndTheResultSaysWhatWasWritten()
    {
        var (fake, context, page) = await Open(Entry("a", 3));
        var applied = new ApplyProjection(ProposalId, false, "Applied", [], "sha256:effect",
            new AppliedLogEntrySummary(ProposalId, "2026-01-01", "Motif", "sha256:intent"));
        fake.ApplyProposalHandler = (_, _) =>
            Task.FromResult(CommandOutcome<ApplyPendingResult>.Success(ApplyPendingResult.AppliedWith(applied, "ok")));
        page.AskToApplyCommand.Execute(null);

        await page.ApplyCommand.ExecuteAsync(null);

        Assert.Single(fake.ApplyProposalRequests);
        Assert.False(page.IsConfirmingApply);
        Assert.True(page.HasReceipt);
        Assert.Equal("Wrote 3 changes to Sample.fwdata.", page.ApplyResultWroteText);
        Assert.Equal(MotifAppStrings.ApplyResultBaselinePending, page.ApplyResultBaselineText);
        context.Evidence.AppliedSinceRefresh = false;
        Assert.Equal(MotifAppStrings.ApplyResultBaselineRecorded, page.ApplyResultBaselineText);
    }

    [Fact]
    public async Task TheBackupLinkOpensTheFieldWorksHelpTopic()
    {
        var launcher = new RecordingLauncher();
        var (_, _, page) = await Open(launcher, Entry("a", 1));

        await page.OpenBackupHelpCommand.ExecuteAsync(null);

        var uri = Assert.Single(launcher.Launched);
        Assert.EndsWith("/Backup_and_Restore/Back_up_this_Project.htm", uri.AbsoluteUri, StringComparison.Ordinal);
    }

    private static Task<(FakeCommandClient Fake, WorkspaceContext Context, ReviewPageModel Page)> Open(
        params ReviewEntry[] entries) => Open(null, entries);

    private static async Task<(FakeCommandClient Fake, WorkspaceContext Context, ReviewPageModel Page)> Open(
        IUriLauncher? launcher, params ReviewEntry[] entries)
    {
        var fake = new FakeCommandClient();
        var proposal = new PendingChangesSnapshot(ProposalId, "revision/one", [], []);
        fake.PendingLoadHandler = (request, _) => Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Success(
            request.ProposalId == ProposalId ? proposal : new PendingChangesSnapshot(null, "none", [], [])));
        fake.ReadTrialCompletesWith(new TrialRecord("job/trial", ProposalId, "sha256:intent", Token,
            null, new Dictionary<string, string> { [AssessmentKinds.ParseTime] = "assessment/trial" }, ["dogs"], false,
            null, JobStatus.Completed, DateTimeOffset.UtcNow, TrialFreshness.Current));
        fake.Review = new ProposalReviewResponse(ProposalId, "sha256:intent", "sha256:revision",
            entries.Select((entry, index) => entry with { Ordinal = index }).ToArray(), null, null, [], 0)
        { EvidenceDigest = "sha256:evidence", Label = "Sound change" };
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        var selection = new SelectionViewModel(fake);
        var context = new WorkspaceContext(selection, new AssessViewModel(fake, selection),
            new ChangesViewModel(fake), fake, new FolderPicker(), new DragSource(), new BaselineViewModel(fake),
            uriLauncher: launcher);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        context.OpenPage(WorkspacePage.Review);
        await context.OpenProposalAsync(ProposalId, CancellationToken.None);
        await page.LoadReviewAsync();
        return (fake, context, page);
    }

    private static GrammarChangeEntry Entry(string key, int operations) =>
        new(key, 0, "Title " + key, Enumerable.Range(0, operations).Select(i => $"op-{key}-{i}").ToArray(), [],
            "digest-" + key, false, "Composer", "1 change", [], [], []);

    private sealed class RecordingLauncher : IUriLauncher
    {
        public List<Uri> Launched { get; } = [];

        public Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            Launched.Add(uri);
            return Task.FromResult(true);
        }
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
