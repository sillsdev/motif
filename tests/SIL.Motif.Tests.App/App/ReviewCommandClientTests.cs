using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App.Services;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ReviewCommandClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task ASeededTrialFlowsThroughReviewApplyWithoutRewritingTheDraft()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        Guid wordformId = Guid.Empty;
        Guid secondWordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
        {
            wordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("review-word", scratch.DefaultVernWs)).Guid;
            secondWordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("review-second", scratch.DefaultVernWs)).Guid;
        });
        new FwDataProjectLoader().Save(scratch);
        var root = Path.Combine(Path.GetDirectoryName(path)!, "review-worker");
        var priorRoot = Environment.GetEnvironmentVariable(RunnerOptions.RootVariable);
        var priorWorker = Environment.GetEnvironmentVariable("MOTIF_WORKER_EXE");
        var priorParser = Environment.GetEnvironmentVariable("MOTIF_PANGLOSS_EXE");
        var priorNamespace = Environment.GetEnvironmentVariable(RunnerOptions.NamespaceVariable);
        var priorIdle = Environment.GetEnvironmentVariable(RunnerOptions.IdleVariable);
        try
        {
            Environment.SetEnvironmentVariable(RunnerOptions.RootVariable, root);
            Environment.SetEnvironmentVariable("MOTIF_WORKER_EXE", BuildOutput.Worker);
            Environment.SetEnvironmentVariable("MOTIF_PANGLOSS_EXE", FakeParser.ExecutablePath);
            Environment.SetEnvironmentVariable(RunnerOptions.NamespaceVariable, Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(RunnerOptions.IdleVariable, "1");
            var client = new CommandClient(root);
            var captured = await client.CaptureBaselineAsync(new BaselineCaptureRequest(path), CancellationToken.None);
            Assert.True(captured.Succeeded, captured.Refusal?.Message);
            var initial = PendingChanges.Load(new PendingChangesRequest(path, "1.0"));
            var added = PendingChanges.Put(new PutPendingChangeRequest(path, "1.0", initial.Value!.Revision,
                new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                    CanonicalId.FromGuid(wordformId).Value, "review-word", OriginPage: "Texts")));
            Assert.True(added.Succeeded, added.Refusal?.Message);
            Assert.Equal("Texts", Assert.Single(added.Value!.Changes).OriginPage);
            Assert.True(ProposalCommands.Label(new LabelRequest(path, "1.0", PendingChanges.DraftName,
                "Correct word spelling")).Succeeded);
            Assert.True(ProposalCommands.Comment(new CommentRequest(path, "1.0", PendingChanges.DraftName,
                "Correct the spelling status of the selected word.")).Succeeded);
            var pending = PendingChanges.Load(new PendingChangesRequest(path, "1.0")).Value!;
            var trial = await client.RunReviewTrialAsync(new ReviewTrialRequest(path, pending.DraftId!,
                pending.Revision, ["review-word"], null), new Progress<ReviewTrialProgress>(),
                CancellationToken.None);
            Assert.True(trial.Succeeded, trial.Refusal?.Message + " " + LastJob(path));
            Assert.True(trial.Value!.EvidenceComplete, trial.Value.NumbersText);

            var applied = await client.ApplyReviewAsync(new ReviewApplyRequest(path, pending.DraftId!,
                pending.Revision), CancellationToken.None);

            Assert.True(applied.Succeeded, applied.Refusal?.Message);
            Assert.Equal(pending.DraftId, applied.Value!.ProposalId);
            Assert.Empty(PendingChanges.Load(new PendingChangesRequest(path, "1.0")).Value!.Changes);
            using var database = ProjectMotifDatabase.Open(path);
            var proposal = new ProposalRepository(database).Get(CanonicalId.Parse(pending.DraftId!));
            Assert.Equal("Correct word spelling", proposal.Label);
            Assert.Equal("Correct the spelling status of the selected word.", proposal.Comment);
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ProposalId = $proposal;";
            command.Parameters.AddWithValue("$proposal", pending.DraftId);
            Assert.Equal(1L, (long)command.ExecuteScalar()!);

            var empty = PendingChanges.Load(new PendingChangesRequest(path, "1.0")).Value!;
            var second = PendingChanges.Put(new PutPendingChangeRequest(path, "1.0", empty.Revision,
                new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                    CanonicalId.FromGuid(secondWordformId).Value, "review-second", OriginPage: "Texts")));
            Assert.True(second.Succeeded, second.Refusal?.Message);
            var stale = await client.ApplyReviewAsync(new ReviewApplyRequest(path, second.Value!.DraftId!,
                "stale-revision"), CancellationToken.None);
            Assert.Equal("review.changes-changed", stale.Refusal?.Code);
            var withoutTrial = await client.ApplyReviewAsync(new ReviewApplyRequest(path, second.Value.DraftId!,
                second.Value.Revision), CancellationToken.None);
            Assert.Equal("apply.not-ready", withoutTrial.Refusal?.Code);
            var reopened = PendingChanges.Load(new PendingChangesRequest(path, "1.0")).Value!;
            Assert.Equal(second.Value.DraftId, reopened.DraftId);
            Assert.Single(reopened.Changes);
            var secondTrial = await client.RunReviewTrialAsync(new ReviewTrialRequest(path, reopened.DraftId!,
                reopened.Revision, ["review-second"], null), new Progress<ReviewTrialProgress>(),
                CancellationToken.None);
            Assert.True(secondTrial.Succeeded, secondTrial.Refusal?.Message + " " + LastJob(path));
            Assert.True(secondTrial.Value!.EvidenceComplete);
            var secondApply = await client.ApplyReviewAsync(new ReviewApplyRequest(path, reopened.DraftId!,
                reopened.Revision), CancellationToken.None);
            Assert.True(secondApply.Succeeded, secondApply.Refusal?.Message);
            var secondProposal = new ProposalRepository(database).Get(CanonicalId.Parse(reopened.DraftId!));
            Assert.Equal("Changes to word analyses", secondProposal.Label);
            Assert.Equal("Changes to word analyses and spelling.", secondProposal.Comment);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RunnerOptions.RootVariable, priorRoot);
            Environment.SetEnvironmentVariable("MOTIF_WORKER_EXE", priorWorker);
            Environment.SetEnvironmentVariable("MOTIF_PANGLOSS_EXE", priorParser);
            Environment.SetEnvironmentVariable(RunnerOptions.NamespaceVariable, priorNamespace);
            Environment.SetEnvironmentVariable(RunnerOptions.IdleVariable, priorIdle);
        }
    }

    [Fact]
    public async Task ApplyWaitsForTheProjectReadGate()
    {
        var client = new CommandClient(Path.GetTempPath());
        var field = typeof(CommandClient).GetField("_projectGate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var gate = (SemaphoreSlim)field.GetValue(client)!;
        await gate.WaitAsync();
        try
        {
            var applying = client.ApplyReviewAsync(new ReviewApplyRequest(
                Path.Combine(Path.GetTempPath(), "missing.fwdata"), "draft/absent", "revision/absent"),
                CancellationToken.None);
            await Task.Delay(100);
            Assert.False(applying.IsCompleted);
        }
        finally
        {
            gate.Release();
        }
    }

    private static string LastJob(string path)
    {
        using var database = ProjectMotifDatabase.Open(path);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status || ': ' || COALESCE(ResultJson, '') FROM Jobs ORDER BY rowid DESC LIMIT 1;";
        return command.ExecuteScalar()?.ToString() ?? "no job";
    }
}
