using System;
using System.IO;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using SIL.LCModel;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group0)]
public sealed class ApplyReconciliationTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ReceiptWriteFailureAfterSavedApplyIsStoreInconsistent()
    {
        var seed = pristine.Seed;
        var gloss = SeededProject.FirstGloss + " receipt boundary";
        string projectPath;
        using (var scratch = pristine.NewScratch())
            projectPath = scratch.ProjectId.Path;

        const string draftName = "receipt-boundary-demo";
        var proposalId = CreateMeasuredProposal(projectPath, draftName,
            CanonicalId.FromGuid(seed.FirstSenseId).Value, gloss);
        var previousFailurePath = Environment.GetEnvironmentVariable("MOTIF_TEST_FAIL_RECEIPT_WRITE_FOR");
        try
        {
            Environment.SetEnvironmentVariable("MOTIF_TEST_FAIL_RECEIPT_WRITE_FOR", projectPath);
            var outcome = ProposalCommands.Apply(new ApplyRequest(
                projectPath, "1.0", proposalId, "test-user", Force: true));

            Assert.False(outcome.Succeeded);
            Assert.Equal("apply.reconciliation-needed", outcome.Refusal!.Code);
            Assert.Equal(FailureReason.StoreInconsistent, outcome.Refusal.Reason);
            Assert.Equal(4, FailureEnvelope.ExitCodeFor(outcome.Refusal.Reason));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOTIF_TEST_FAIL_RECEIPT_WRITE_FOR", previousFailurePath);
        }

        AssertGlossOnDisk(projectPath, seed.FirstSenseId, NewLangProjFixture.AnalysisTag, gloss);

        using var failedStore = ProjectMotifDatabase.Open(projectPath);
        using var connection = failedStore.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ProposalId = $proposal;";
        command.Parameters.AddWithValue("$proposal", proposalId);
        Assert.Equal(0L, (long)command.ExecuteScalar()!);
    }

    private static string CreateMeasuredProposal(string projectPath, string draftName, string target, string gloss)
    {
        Assert.True(ProposalCommands.New(new NewDraftRequest(
            projectPath, "1.0", draftName, null)).Succeeded);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            projectPath, "1.0", draftName, target, NewLangProjFixture.AnalysisTag,
            gloss)).Succeeded);
        DraftRationale.Author(projectPath, draftName, "Clarify a gloss",
            "Record the intended lexical meaning before applying the Proposal.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(projectPath, "1.0", draftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        Assert.True(DryRunJobRunner.Run(projectPath, "1.0", finalized.Value!.ProposalId).Succeeded);
        return finalized.Value.ProposalId;
    }

    private static void AssertGlossOnDisk(string projectPath, Guid senseGuid, string writingSystem, string expected)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(projectPath);
        var wsHandle = cache.WritingSystemFactory.GetWsFromStr(writingSystem);
        var sense = cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(senseGuid);
        Assert.Equal(expected, sense.Gloss.get_String(wsHandle).Text);
    }

}
