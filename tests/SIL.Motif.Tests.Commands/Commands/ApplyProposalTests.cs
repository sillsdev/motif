using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class ApplyProposalTests(PristineProjectFixture pristine)
{
    private static readonly TimeSpan StepBound = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task FinalizesADraftThenApplies()
    {
        var (path, pending) = await TriedPendingChange("finalize-then-apply");

        var applied = await Task.Run(() => PendingChangesWorkflow.Apply(
            new ApplyProposalRequest(path, pending.DraftId!, pending.Revision))).WaitAsync(StepBound);

        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        Assert.True(applied.Value!.Applied);
        Assert.Equal(pending.DraftId, applied.Value.Receipt!.ProposalId);
        Assert.Equal("Marked 1 word as incorrectly spelled.", applied.Value.Summary);
        Assert.Empty(PendingChanges.Load(new PendingChangesRequest(path, ProductVersion)).Value!.Changes);
    }

    [Fact]
    public async Task AppliesAFinalizedProposalFromItsCurrentTrial()
    {
        var (path, pending) = await TriedPendingChange("apply-finalized");
        Assert.True(ProposalCommands.Finalize(new FinalizeRequest(path, ProductVersion,
            PendingChanges.DraftName, pending.Revision)).Succeeded);

        var applied = await Task.Run(() => PendingChangesWorkflow.Apply(
            new ApplyProposalRequest(path, pending.DraftId!, null))).WaitAsync(StepBound);

        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        Assert.True(applied.Value!.Applied);
    }

    [Fact]
    public async Task AProposalReopenedForReviewIsAppliedUnderItsOwnId()
    {
        var (path, pending) = await TriedPendingChange("apply-reopened");
        Assert.True(ProposalCommands.Finalize(new FinalizeRequest(path, ProductVersion,
            PendingChanges.DraftName, pending.Revision)).Succeeded);
        var reopened = ProposalCommands.Reopen(new ReopenRequest(path, ProductVersion, "review/" + pending.DraftId,
            pending.DraftId!));
        Assert.True(reopened.Succeeded, reopened.Refusal?.Message);

        var applied = await Task.Run(() => PendingChangesWorkflow.Apply(
            new ApplyProposalRequest(path, pending.DraftId!, null))).WaitAsync(StepBound);

        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        Assert.Equal(pending.DraftId, applied.Value!.Receipt!.ProposalId);
    }

    [Fact]
    public void ARefusedApplyReopensANamedDraftUnderItsOwnNameAndId()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        scratch.Dispose();
        var created = ProposalCommands.New(new NewDraftRequest(path, ProductVersion, "assistant-draft", "A gloss"));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, ProductVersion, "assistant-draft",
            CanonicalId.Mint().Value, "en", "a gloss")).Succeeded);
        DraftRationale.Author(path, "assistant-draft", "A gloss", "Why the gloss is right.");

        var outcome = PendingChangesWorkflow.Apply(new ApplyRequest(path, ProductVersion,
            created.Value!.ProposalId));

        Assert.Equal("apply.dry-run-missing", outcome.Refusal?.Code);
        using var database = ProjectMotifDatabase.Open(path);
        Assert.Equal(created.Value.ProposalId, new ProposalRepository(database).GetDraft("assistant-draft")
            .ProposalId.Value);
    }

    [Fact]
    public void TheDeveloperApplyAndTheTwoReleasedOnesShareOneResponseShape() =>
        Assert.All(new[] { "apply", "apply --proposal", "apply --all-pending" }, name =>
            Assert.Equal(typeof(ApplyPendingResult), SIL.Motif.Commands.Catalog.CommandCatalog.All
                .Single(command => command.Name == name).ResponseType));

    [Fact]
    public async Task RefusesWhenTheTrialIsNotCurrent()
    {
        var (path, pending) = await TriedPendingChange("stale-trial");
        var added = ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, ProductVersion,
            PendingChanges.DraftName, CanonicalId.Mint().Value, "en", "a gloss added after the Trial"));
        Assert.True(added.Succeeded, added.Refusal?.Message);

        var outcome = PendingChangesWorkflow.Apply(new ApplyProposalRequest(path, pending.DraftId!, null));

        Assert.Equal("apply.trial-stale", outcome.Refusal?.Code);
        Assert.NotEmpty(PendingChanges.Load(new PendingChangesRequest(path, ProductVersion)).Value!.Changes);
    }

    [Fact]
    public async Task RefusesWithoutATrial()
    {
        var (path, pending) = await NewPendingChange("no-trial");

        var outcome = PendingChangesWorkflow.Apply(new ApplyProposalRequest(path, pending.DraftId!, null));

        Assert.Equal("apply.trial-missing", outcome.Refusal?.Code);
        Assert.Equal(pending.DraftId, PendingChanges.Load(new PendingChangesRequest(path, ProductVersion))
            .Value!.DraftId);
    }

    [Fact]
    public void AStaleRevisionIsRefusedEvenWhenTheProposalIsEmpty()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        scratch.Dispose();
        var created = ProposalCommands.New(new NewDraftRequest(path, ProductVersion, "emptied", "Emptied"));
        Assert.True(created.Succeeded, created.Refusal?.Message);

        var outcome = PendingChangesWorkflow.Apply(
            new ApplyProposalRequest(path, created.Value!.ProposalId, "sha256:stale"));

        Assert.Equal("apply.changes-changed", outcome.Refusal?.Code);
    }

    [Fact]
    public void AllPendingKeepsItsResponseShape()
    {
        using var response = JsonDocument.Parse(ProjectionJson.Serialize(ApplyPendingResult.NothingPending));

        Assert.Equal(["ok", "applied", "summary"],
            response.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.False(response.RootElement.GetProperty("applied").GetBoolean());
        Assert.Equal("Nothing to apply.", response.RootElement.GetProperty("summary").GetString());
    }

    private async Task<(string Path, PendingChangesSnapshot Pending)> TriedPendingChange(
        string word)
    {
        var (path, pending) = await NewPendingChange(word);
        await using var runner = new InProcessRunnerLauncher(IsolatedRunner.Options(RootFor(path)));
        var measured = await PendingChangesWorkflow.Measure(new MeasurePendingRequest(path, pending.DraftId,
            pending.Revision, [word]), new Progress<MeasureProgress>(), CancellationToken.None, StepBound, runner)
            .WaitAsync(StepBound + TimeSpan.FromSeconds(30));
        Assert.True(measured.Succeeded, measured.Refusal?.Message);
        await runner.WhenIdleAsync().WaitAsync(StepBound);
        return (path, PendingChanges.Load(new PendingChangesRequest(path, ProductVersion)).Value!);
    }

    private Task<(string Path, PendingChangesSnapshot Pending)> NewPendingChange(string word)
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
            wordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(word, scratch.DefaultVernWs)).Guid);
        new FwDataProjectLoader().Save(scratch);
        var root = RootFor(path);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var pending = PendingChanges.Put(new PutPendingChangeRequest(path, ProductVersion,
            PendingChanges.Load(new PendingChangesRequest(path, ProductVersion)).Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling", CanonicalId.FromGuid(wordformId).Value,
                word, OriginPage: "Texts"))).Value!;
        return Task.FromResult((path, pending));
    }

    private static string RootFor(string projectPath)
    {
        var root = Path.Combine(Path.GetDirectoryName(projectPath)!, "apply-proposal-root");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string ProductVersion => MotifProductVersion.CurrentText;
}
