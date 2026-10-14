using System;
using System.IO;
using System.Linq;
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
    [Theory]
    [InlineData("declined", "Declined")]
    [InlineData("withdrawn", "Withdrawn")]
    [InlineData("superseded", "Superseded")]
    public void ApplyRefusesAClosedProposalWithoutChangingTheProject(string status, string statusWord)
    {
        var seed = pristine.Seed;
        string projectPath;
        using (var scratch = pristine.NewScratch())
            projectPath = scratch.ProjectId.Path;

        var proposalId = CreateMeasuredProposal(projectPath, "closed-" + status,
            CanonicalId.FromGuid(seed.FirstSenseId).Value, SeededProject.FirstGloss + " proposed");
        string? replacementId = null;
        switch (status)
        {
            case "declined":
                Assert.True(ProposalCommands.Reject(new RejectRequest(projectPath, "1.0", proposalId)).Succeeded);
                break;
            case "withdrawn":
                Assert.True(ProposalCommands.Withdraw(new RejectRequest(projectPath, "1.0", proposalId)).Succeeded);
                break;
            case "superseded":
                replacementId = CreateMeasuredProposal(projectPath, "replacement",
                    CanonicalId.FromGuid(seed.FirstSenseId).Value, SeededProject.FirstGloss + " replacement");
                Assert.True(ProposalCommands.Supersede(new SupersedeRequest(
                    projectPath, "1.0", proposalId, replacementId)).Succeeded);
                break;
        }

        var outcome = ProposalCommands.Apply(new ApplyRequest(projectPath, "1.0", proposalId));

        Assert.False(outcome.Succeeded);
        Assert.Equal("proposal.invalid-status", outcome.Refusal?.Code);
        Assert.Equal(FailureReason.Refused, outcome.Refusal?.Reason);
        Assert.Contains(statusWord, outcome.Refusal!.Message, StringComparison.Ordinal);
        if (replacementId is null)
            Assert.Contains("Reopen", outcome.Refusal.Message, StringComparison.Ordinal);
        else
        {
            Assert.Contains(replacementId, outcome.Refusal.Message, StringComparison.Ordinal);
            Assert.Contains("Open", outcome.Refusal.Message, StringComparison.Ordinal);
        }
        AssertGlossOnDisk(projectPath, seed.FirstSenseId, NewLangProjFixture.AnalysisTag, SeededProject.FirstGloss);
    }

    [Fact]
    public void ADeferredProposalCanStillBeApplied()
    {
        var seed = pristine.Seed;
        string projectPath;
        using (var scratch = pristine.NewScratch())
            projectPath = scratch.ProjectId.Path;

        var gloss = SeededProject.FirstGloss + " deferred";
        var proposalId = CreateMeasuredProposal(projectPath, "deferred-apply",
            CanonicalId.FromGuid(seed.FirstSenseId).Value, gloss);
        Assert.True(ProposalCommands.Defer(new DeferRequest(projectPath, "1.0", proposalId)).Succeeded);

        var outcome = ProposalCommands.Apply(new ApplyRequest(projectPath, "1.0", proposalId, Force: true));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(proposalId, outcome.Value!.ProposalId);
        AssertGlossOnDisk(projectPath, seed.FirstSenseId, NewLangProjFixture.AnalysisTag, gloss);
    }

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
                projectPath, "1.0", proposalId, Force: true));

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

    // After the project is saved, a busy store is an unknown outcome to reconcile, never a Busy to retry.
    [Fact]
    public void ABusyStoreAfterApplySavedTheProjectNeedsReconciliationNotARetry()
    {
        var seed = pristine.Seed;
        var gloss = SeededProject.FirstGloss + " busy receipt";
        string projectPath;
        using (var scratch = pristine.NewScratch())
            projectPath = scratch.ProjectId.Path;

        var proposalId = CreateMeasuredProposal(projectPath, "busy-receipt-demo",
            CanonicalId.FromGuid(seed.FirstSenseId).Value, gloss);
        var previous = Environment.GetEnvironmentVariable("MOTIF_TEST_BUSY_RECEIPT_WRITE_FOR");
        try
        {
            Environment.SetEnvironmentVariable("MOTIF_TEST_BUSY_RECEIPT_WRITE_FOR", projectPath);
            var outcome = ProposalCommands.Apply(new ApplyRequest(projectPath, "1.0", proposalId, Force: true));

            Assert.False(outcome.Succeeded);
            Assert.Equal("apply.reconciliation-needed", outcome.Refusal!.Code);
            Assert.Equal(FailureReason.StoreInconsistent, outcome.Refusal.Reason);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOTIF_TEST_BUSY_RECEIPT_WRITE_FOR", previous);
        }
        AssertGlossOnDisk(projectPath, seed.FirstSenseId, NewLangProjFixture.AnalysisTag, gloss);
    }

    [Fact]
    public void FailureAfterReceiptRecordingStillReturnsAppliedSuccess()
    {
        var seed = pristine.Seed;
        var gloss = SeededProject.FirstGloss + " post-receipt failure";
        string projectPath;
        using (var scratch = pristine.NewScratch())
            projectPath = scratch.ProjectId.Path;

        var proposalId = CreateMeasuredProposal(projectPath, "post-receipt-demo",
            CanonicalId.FromGuid(seed.FirstSenseId).Value, gloss);
        var previousFailurePath = Environment.GetEnvironmentVariable("MOTIF_TEST_FAIL_AFTER_RECEIPT_FOR");
        try
        {
            Environment.SetEnvironmentVariable("MOTIF_TEST_FAIL_AFTER_RECEIPT_FOR", projectPath);
            var outcome = ProposalCommands.Apply(new ApplyRequest(
                projectPath, "1.0", proposalId, Force: true));

            Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
            Assert.Equal(proposalId, outcome.Value!.ProposalId);
            Assert.Contains("post-receipt housekeeping could not be completed", outcome.Value.ResultNote,
                StringComparison.Ordinal);
            var log = ProjectDiagnosticLog.PathFor(projectPath);
            Assert.Contains(log, outcome.Value.ResultNote, StringComparison.Ordinal);
            Assert.Contains("Injected post-receipt failure.", File.ReadAllText(log), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOTIF_TEST_FAIL_AFTER_RECEIPT_FOR", previousFailurePath);
        }

        AssertGlossOnDisk(projectPath, seed.FirstSenseId, NewLangProjFixture.AnalysisTag, gloss);
        using var store = ProjectMotifDatabase.Open(projectPath);
        using var connection = store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ProposalId = $proposal;";
        command.Parameters.AddWithValue("$proposal", proposalId);
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void ACacheThatFailsToCloseAfterApplyStillReturnsAppliedSuccessWithANote()
    {
        var seed = pristine.Seed;
        var gloss = SeededProject.FirstGloss + " dispose failure";
        string projectPath;
        using (var scratch = pristine.NewScratch())
            projectPath = scratch.ProjectId.Path;

        var proposalId = CreateMeasuredProposal(projectPath, "dispose-failure-demo",
            CanonicalId.FromGuid(seed.FirstSenseId).Value, gloss);
        var previousFailurePath = Environment.GetEnvironmentVariable("MOTIF_TEST_FAIL_CACHE_DISPOSE_FOR");
        try
        {
            Environment.SetEnvironmentVariable("MOTIF_TEST_FAIL_CACHE_DISPOSE_FOR", projectPath);
            var outcome = ProposalCommands.Apply(new ApplyRequest(projectPath, "1.0", proposalId, Force: true));

            Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
            Assert.Contains("could not close the project cleanly", outcome.Value!.ResultNote, StringComparison.Ordinal);
            Assert.Contains("Injected cache disposal failure.",
                File.ReadAllText(ProjectDiagnosticLog.PathFor(projectPath)), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOTIF_TEST_FAIL_CACHE_DISPOSE_FOR", previousFailurePath);
        }

        AssertGlossOnDisk(projectPath, seed.FirstSenseId, NewLangProjFixture.AnalysisTag, gloss);
    }

    [Fact]
    public void ApplysUnitOfWorkCannotBeCancelledByCtrlC()
    {
        // The CLI's Ctrl+C token reaches the Dry Run wait only; a cancelled Apply would be half a unit of work.
        var applies = typeof(ProposalCommands).GetMethods().Where(method => method.Name == "Apply").ToArray();

        Assert.NotEmpty(applies);
        Assert.All(applies, method => Assert.DoesNotContain(method.GetParameters(),
            parameter => parameter.ParameterType == typeof(System.Threading.CancellationToken)));
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
